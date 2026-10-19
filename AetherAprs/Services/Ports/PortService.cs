// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Configuration;
using AetherAprs.Data;
using AetherAprs.Data.Mappers;
using AetherAprs.Models.Aprs;
using AetherAprs.Models.Aprs.Packets;
using AetherAprs.Modems.Aprs;
using AetherAprs.Modems.Kiss;
using AetherAprs.Services.Configuration;
using AetherAprs.Services.Contracts;
using AetherAprs.Services.Packets;
using AetherAprs.Services.Platform;
using AetherAprs.Transports.Kiss;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace AetherAprs.Services.Ports;

public class PortService : IPortService, IAsyncDisposable
{
    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;
    private readonly IConfigurationService _configurationService;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<PortService> _logger;
    private readonly IKissStreamFactory _kissStreamFactory;
    private readonly IPacketStorageService _packetStorageService;
    private readonly IForegroundService _foregroundService;
    private readonly IDigipeaterService _digipeaterService;

    // Guards _ports and _activeSessions; also serializes modem start/stop per service.
    private readonly Lock _stateLock = new();
    private readonly Dictionary<Guid, ActivePortSession> _activeSessions = new();
    private readonly List<PortConfig> _ports;

    private readonly CancellationTokenSource _disposalCts = new();
    private readonly Channel<QueuedPacket> _packetChannel;
    private readonly Task _packetProcessorTask;

    private sealed record QueuedPacket(Guid PortId, AprsPacket Packet);

    public PortService(
        IDbContextFactory<AppDbContext> dbContextFactory,
        IConfigurationService configurationService,
        ILoggerFactory loggerFactory,
        ILogger<PortService> logger,
        IKissStreamFactory kissStreamFactory,
        IPacketStorageService packetStorageService,
        IForegroundService foregroundService,
        IDigipeaterService digipeaterService)
    {
        _dbContextFactory = dbContextFactory;
        _configurationService = configurationService;
        _loggerFactory = loggerFactory;
        _logger = logger;
        _kissStreamFactory = kissStreamFactory;
        _packetStorageService = packetStorageService;
        _foregroundService = foregroundService;
        _digipeaterService = digipeaterService;

        // Bound the queue so a burst cannot grow it unboundedly; the single consumer
        // also guarantees packets are persisted and published in receive order.
        _packetChannel = Channel.CreateBounded<QueuedPacket>(new BoundedChannelOptions(1024)
        {
            SingleReader = true,
            FullMode = BoundedChannelFullMode.Wait
        });
        _packetProcessorTask = Task.Run(ProcessPacketQueueAsync);

        _ports = [];
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var loaded = await LoadPortsAsync(cancellationToken).ConfigureAwait(false);
        lock (_stateLock)
        {
            _ports.Clear();
            _ports.AddRange(loaded);
        }
        PortsChanged?.Invoke(this, EventArgs.Empty);
    }

    public IReadOnlyList<PortConfig> Ports
    {
        get
        {
            lock (_stateLock)
            {
                return [.. _ports];
            }
        }
    }

    public event EventHandler? PortsChanged;

    public event EventHandler<PortPacketReceivedEventArgs>? PacketReceived;

    public async Task AddPortAsync(PortConfig port)
    {
        ArgumentNullException.ThrowIfNull(port);

        lock (_stateLock)
        {
            _ports.Add(port);
        }

        await PersistPortAsync(port, default).ConfigureAwait(false);
        PortsChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task UpdatePortAsync(PortConfig updatedPort)
    {
        ArgumentNullException.ThrowIfNull(updatedPort);

        ActivePortSession? sessionToRestart = null;
        PortConfig? port;
        bool startAfterUpdate;

        lock (_stateLock)
        {
            port = _ports.FirstOrDefault(p => p.Id == updatedPort.Id);
            if (port is null)
            {
                return;
            }

            startAfterUpdate = _activeSessions.ContainsKey(port.Id) || port.IsEnabled;
            if (_activeSessions.TryGetValue(port.Id, out var session))
            {
                // Detach the session from the running state before restarting.
                _activeSessions.Remove(port.Id);
                sessionToRestart = session;
            }

            CopyPortProperties(updatedPort, port);
        }

        await PersistPortAsync(port, default).ConfigureAwait(false);

        if (sessionToRestart is not null)
        {
            _logger.LogDebug(
                "Restarting running port {PortName} ({PortId}) after settings update.",
                port.Name,
                port.Id);
            await StopSessionAsync(sessionToRestart).ConfigureAwait(false);
        }

        if (startAfterUpdate && port.IsEnabled)
        {
            await StartModemAsync(port).ConfigureAwait(false);
        }

        await UpdateForegroundServiceAsync().ConfigureAwait(false);
        PortsChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task RemovePortAsync(Guid id)
    {
        // Stop if running
        await StopModemAsync(id).ConfigureAwait(false);

        lock (_stateLock)
        {
            var port = _ports.FirstOrDefault(p => p.Id == id);
            if (port is not null)
            {
                _ports.Remove(port);
            }
        }

        await DeletePortAsync(id, default).ConfigureAwait(false);
        PortsChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task SetPortEnabledAsync(Guid id, bool enabled)
    {
        PortConfig? port;
        lock (_stateLock)
        {
            port = _ports.FirstOrDefault(p => p.Id == id);
        }

        if (port is null)
        {
            return;
        }

        port.IsEnabled = enabled;
        await PersistPortAsync(port, default).ConfigureAwait(false);

        if (enabled)
        {
            var started = await StartModemAsync(port).ConfigureAwait(false);
            if (!started)
            {
                port.IsEnabled = false;
                await PersistPortAsync(port, default).ConfigureAwait(false);
                PortsChanged?.Invoke(this, EventArgs.Empty);
                throw new InvalidOperationException($"Failed to start port '{port.Name}'.");
            }
        }
        else
        {
            await StopModemAsync(id).ConfigureAwait(false);
        }

        // Start/stop foreground service based on how many ports are running
        await UpdateForegroundServiceAsync().ConfigureAwait(false);
        PortsChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task SetPortShowOnMapAsync(Guid id, bool showOnMap)
    {
        PortConfig? port;
        lock (_stateLock)
        {
            port = _ports.FirstOrDefault(p => p.Id == id);
            if (port is null || port.ShowOnMap == showOnMap)
            {
                return;
            }

            port.ShowOnMap = showOnMap;
        }

        await PersistPortAsync(port, default).ConfigureAwait(false);
        PortsChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task SendPacketAsync(Guid id, AprsPacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);

        // Resolve (and auto-start if needed) the session under the state lock so
        // two concurrent sends cannot both pass the check-then-start race.
        ActivePortSession session;
        lock (_stateLock)
        {
            if (!_activeSessions.TryGetValue(id, out session!))
            {
                var portToStart = _ports.FirstOrDefault(p => p.Id == id);
                if (portToStart is null || !portToStart.IsEnabled)
                {
                    throw new InvalidOperationException($"Port {id} is not running.");
                }
            }
        }

        if (session is null)
        {
            PortConfig? portToStart;
            lock (_stateLock)
            {
                portToStart = _ports.FirstOrDefault(p => p.Id == id);
            }

            if (portToStart is not null)
            {
                await StartModemAsync(portToStart).ConfigureAwait(false);
            }
        }

        lock (_stateLock)
        {
            if (!_activeSessions.TryGetValue(id, out session!))
            {
                throw new InvalidOperationException($"Port {id} is not running.");
            }
        }

        var port = FindPortById(id);
        var portName = port?.Name ?? id.ToString();
        var infoField = AprsInfoFieldSerializer.FormatInfoField(packet);

        await session.Modem.SendAsync(packet).ConfigureAwait(false);

        // Update foreground service state after auto-starting a stopped port.
        bool sessionExists;
        lock (_stateLock)
        {
            sessionExists = _activeSessions.ContainsKey(id);
        }

        if (port is not null && !sessionExists)
        {
            // Session no longer present (stopped concurrently); nothing to update.
        }
        else
        {
            await UpdateForegroundServiceAsync().ConfigureAwait(false);
        }

        if (packet is PositionPacket position)
        {
            _logger.LogInformation(
                "Transmitted APRS position on port {PortName} ({PortId}): Latitude={Latitude:F5}, Longitude={Longitude:F5}, Altitude={Altitude}, Course={Course}, Speed={Speed}, Comment={Comment}, Raw={Raw}",
                portName,
                id,
                position.Location.Latitude,
                position.Location.Longitude,
                position.Altitude,
                position.Course,
                position.Speed,
                position.Comment,
                infoField);
        }
        else
        {
            _logger.LogInformation(
                "Transmitted APRS packet on port {PortName} ({PortId}), Type={PacketType}, Raw={Raw}",
                portName,
                id,
                packet.GetType().Name,
                infoField);
        }
    }

    public async Task StartAllEnabledPortsAsync()
    {
        List<PortConfig> portsToStart;
        lock (_stateLock)
        {
            portsToStart = [.. _ports.Where(p => p.IsEnabled)];
        }

        foreach (var port in portsToStart)
        {
            await StartModemAsync(port).ConfigureAwait(false);
        }
    }

    public async Task StopAllPortsAsync()
    {
        List<ActivePortSession> sessions;
        lock (_stateLock)
        {
            sessions = [.. _activeSessions.Values];
            _activeSessions.Clear();
        }

        foreach (var session in sessions)
        {
            await StopSessionAsync(session).ConfigureAwait(false);
        }

        await UpdateForegroundServiceAsync().ConfigureAwait(false);
    }

    private PortConfig? FindPortById(Guid id)
    {
        lock (_stateLock)
        {
            return _ports.FirstOrDefault(p => p.Id == id);
        }
    }

    private async Task<List<PortConfig>> LoadPortsAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var records = await db.Ports.AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);
        return [.. records.Select(PortRecordMapper.ToConfig)];
    }

    private async Task PersistPortAsync(PortConfig port, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var existing = await db.Ports.FindAsync([port.Id], cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            db.Ports.Add(PortRecordMapper.ToRecord(port));
        }
        else
        {
            PortRecordMapper.CopyTo(existing, port);
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            // Two concurrent saves for the same port (e.g. UI double-tap): the row
            // was inserted by the other save — retry as an update.
            db.ChangeTracker.Clear();
            var retry = await db.Ports.FindAsync([port.Id], cancellationToken).ConfigureAwait(false);
            if (retry is not null)
            {
                PortRecordMapper.CopyTo(retry, port);
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task DeletePortAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var existing = await db.Ports.FindAsync([id], cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            db.Ports.Remove(existing);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static void CopyPortProperties(PortConfig source, PortConfig target)
    {
        target.Name = source.Name;
        target.IsEnabled = source.IsEnabled;
        target.IsRx = source.IsRx;
        target.IsTx = source.IsTx;
        target.ShowOnMap = source.ShowOnMap;
        target.AllowDigipeat = source.AllowDigipeat;
        target.TypeSettings = source.TypeSettings;
    }

    private async Task<bool> StartModemAsync(PortConfig port)
    {
        lock (_stateLock)
        {
            if (_activeSessions.ContainsKey(port.Id))
            {
                _logger.LogDebug("Port {PortId} ({PortName}) is already running.", port.Id, port.Name);
                return true;
            }
        }

        try
        {
            switch (port.TypeSettings)
            {
                case AprsIsSettings aprsIsSettings:
                    {
                        var settings = _configurationService.GetSettingsSnapshot();
                        var callsign = settings.Aprs.Callsign;
                        var ssid = (int)settings.Aprs.Ssid;
                        var fullCallsign = new Callsign(
                            callsign,
                            ssid > 0 ? ssid : null);

                        var modem = new AprsIsModem(
                            aprsIsSettings.Server,
                            aprsIsSettings.ServerPort,
                            fullCallsign,
                            aprsIsSettings.Passcode,
                            aprsIsSettings.Filter,
                            _loggerFactory.CreateLogger<AprsIsModem>());

                        EventHandler<AprsPacket> packetHandler = (_, packet) => RaisePacketReceived(port.Id, packet);
                        modem.PacketReceived += packetHandler;
                        modem.ReceiveError += OnModemReceiveError;
                        modem.Start();

                        lock (_stateLock)
                        {
                            _activeSessions[port.Id] = new ActivePortSession
                            {
                                PortId = port.Id,
                                Modem = modem,
                                PacketHandler = packetHandler
                            };
                        }

                        _logger.LogInformation("Started modem for port {PortName} ({PortId}).", port.Name, port.Id);
                        break;
                    }
                case KissSettings kiss:
                    {
                        var stream = await _kissStreamFactory.OpenAsync(kiss).ConfigureAwait(false);
                        var kissModem = new KissModem(stream, _loggerFactory);
                        var modem = new AprsRfModem(kissModem, _loggerFactory);

                        EventHandler<AprsPacket> packetHandler = (_, packet) => RaisePacketReceived(port.Id, packet);
                        modem.PacketReceived += packetHandler;
                        modem.ReceiveError += OnModemReceiveError;
                        modem.Start();

                        lock (_stateLock)
                        {
                            _activeSessions[port.Id] = new ActivePortSession
                            {
                                PortId = port.Id,
                                Modem = modem,
                                KissModem = kissModem,
                                PacketHandler = packetHandler
                            };
                        }

                        _logger.LogInformation("Started KISS modem for port {PortName} ({PortId}).", port.Name, port.Id);
                        break;
                    }
                default:
                    _logger.LogWarning(
                        "Failed to start modem for port {PortName} ({PortId}): unknown or missing TypeSettings.",
                        port.Name,
                        port.Id);
                    return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start modem for port {PortName} ({PortId}).", port.Name, port.Id);
            return false;
        }
    }

    private async Task StopModemAsync(Guid id)
    {
        ActivePortSession? session;
        lock (_stateLock)
        {
            if (!_activeSessions.Remove(id, out session))
            {
                return;
            }
        }

        await StopSessionAsync(session).ConfigureAwait(false);
    }

    private async Task StopSessionAsync(ActivePortSession session)
    {
        try
        {
            session.Modem.PacketReceived -= session.PacketHandler;
            session.Modem.ReceiveError -= OnModemReceiveError;
            await session.Modem.StopAsync().ConfigureAwait(false);
        }
        finally
        {
            if (session.Modem is IAsyncDisposable modemDisposable)
            {
                await modemDisposable.DisposeAsync().ConfigureAwait(false);
            }

            if (session.KissModem is not null)
            {
                await session.KissModem.DisposeAsync().ConfigureAwait(false);
            }
        }

        _logger.LogInformation("Stopped modem for port {PortId}.", session.PortId);
    }

    private async Task UpdateForegroundServiceAsync()
    {
        bool hasEnabledPorts;
        lock (_stateLock)
        {
            hasEnabledPorts = _activeSessions.Count > 0;
        }

        if (hasEnabledPorts && !_foregroundService.IsRunning)
        {
            await _foregroundService.StartAsync().ConfigureAwait(false);
            _logger.LogInformation("Started foreground service");
        }
        else if (!hasEnabledPorts && _foregroundService.IsRunning)
        {
            await _foregroundService.StopAsync().ConfigureAwait(false);
            _logger.LogInformation("Stopped foreground service");
        }
    }

    private void RaisePacketReceived(Guid portId, AprsPacket packet)
    {
        _logger.LogInformation(
            "Packet received on port {PortId} from {Source}: {Raw}",
            portId,
            packet.Source,
            packet.Raw);
        if (packet is UnknownPacket)
        {
            _logger.LogWarning(
                "Unknown APRS packet on port {PortId} from {Source}: {Raw}",
                portId,
                packet.Source,
                packet.Raw);
        }

        // Queue for ordered sequential processing: storage → event → digipeat.
        // A bounded channel preserves receive order (Id-ordering assumptions in the
        // query service) and prevents unbounded Task.Run fan-out under bursts.
        if (!_packetChannel.Writer.TryWrite(new QueuedPacket(portId, packet)))
        {
            _logger.LogWarning(
                "Packet queue full; dropping packet from {Source} on port {PortId}.",
                packet.Source,
                portId);
        }
    }

    private async Task ProcessPacketQueueAsync()
    {
        try
        {
            await foreach (var queued in _packetChannel.Reader.ReadAllAsync(_disposalCts.Token).ConfigureAwait(false))
            {
                await ProcessPacketAsync(queued.PortId, queued.Packet).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during disposal
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Packet processing queue terminated unexpectedly.");
        }
    }

    private async Task ProcessPacketAsync(Guid portId, AprsPacket packet)
    {
        try
        {
            await _packetStorageService.StorePacketAsync(packet, portId, isOutbound: false, _disposalCts.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected during disposal
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to store packet from {Source}", packet.Source);
        }

        // Raise event after storage so consumers see persisted packets in order.
        try
        {
            PacketReceived?.Invoke(this, new PortPacketReceivedEventArgs
            {
                PortId = portId,
                Packet = packet
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception in PacketReceived subscriber for packet from {Source}", packet.Source);
        }

        // Handle digipeating
        try
        {
            await HandleDigipeatAsync(portId, packet).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to digipeat packet from {Source}", packet.Source);
        }
    }

    private async Task HandleDigipeatAsync(Guid sourcePortId, AprsPacket packet)
    {
        try
        {
            // Get source port info
            PortConfig? sourcePort;
            lock (_stateLock)
            {
                sourcePort = _ports.FirstOrDefault(p => p.Id == sourcePortId);
            }

            if (sourcePort is null)
            {
                return;
            }

            bool sourceIsAprsIs = sourcePort.TypeSettings is AprsIsSettings;

            // Build list of available ports (running and TX-capable)
            List<PortInfo> availablePorts;
            lock (_stateLock)
            {
                availablePorts = [.. _ports
                    .Where(p => _activeSessions.ContainsKey(p.Id))
                    .Select(p => new PortInfo(
                        p.Id,
                        p.TypeSettings is AprsIsSettings,
                        p.IsTx,
                        p.AllowDigipeat))];
            }

            // Get digipeat targets
            var targets = _digipeaterService.GetDigipeatTargets(
                packet,
                sourcePortId,
                sourceIsAprsIs,
                availablePorts);

            // Send to each target port
            foreach (var target in targets)
            {
                try
                {
                    await SendPacketAsync(target.PortId, target.Packet).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Expected during disposal
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Failed to digipeat packet from {Source} to port {TargetPortId}",
                        packet.Source,
                        target.PortId);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in digipeat handler for packet from {Source}", packet.Source);
        }
    }

    private void OnModemReceiveError(object? sender, Exception exception)
    {
        _logger.LogWarning(exception, "Receive error on modem.");
    }

    public async ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        _logger.LogInformation("Disposing PortService and stopping all active ports.");

        // Stop packet processing first, then ports. The CTS is intentionally not
        // disposed: queued callbacks may still reference its token, and disposing
        // it can throw ObjectDisposedException synchronously into modem threads.
        _disposalCts.Cancel();
        _packetChannel.Writer.TryComplete();

        try
        {
            await _packetProcessorTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected during disposal
        }

        await StopAllPortsAsync().ConfigureAwait(false);

        // Stop foreground service on disposal
        if (_foregroundService.IsRunning)
        {
            await _foregroundService.StopAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// A running port's modem session with its owned resources.
    /// </summary>
    private sealed class ActivePortSession
    {
        public required Guid PortId { get; init; }

        public required IAprsModem Modem { get; init; }

        /// <summary>
        /// Owned KISS modem when the session wraps an <see cref="AprsRfModem"/>.
        /// </summary>
        public KissModem? KissModem { get; init; }

        public required EventHandler<AprsPacket> PacketHandler { get; init; }
    }
}
