// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Configuration;
using AetherAprs.Models.Aprs;
using AetherAprs.Models.Aprs.Packets;
using AetherAprs.Modems.Afsk;
using AetherAprs.Modems.Aprs;
using AetherAprs.Modems.Kiss;
using AetherAprs.Services.Configuration;
using AetherAprs.Services.Contracts;
using AetherAprs.Services.Diagnostics;
using AetherAprs.Services.Location;
using AetherAprs.Services.Packets;
using AetherAprs.Services.Platform;
using AetherAprs.Transports.Kiss;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace AetherAprs.Services.Ports;

public class PortService : IPortService, IPortServiceTelemetry, IPortSessionRuntime, IAsyncDisposable
{
    private readonly IPortRegistry _portRegistry;
    private readonly IConfigurationService _configurationService;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<PortService> _logger;
    private readonly IKissStreamFactory _kissStreamFactory;
    private readonly IPacketStorageService _packetStorageService;
    private readonly IKeepAliveService _keepAliveService;
    private readonly IDigipeaterService _digipeaterService;
    private readonly IAudioStreamService _audioStreamService;
    private readonly IAudioPlaybackService _audioPlaybackService;
    private readonly IPttService _pttService;
    private readonly ILocationTrackingService? _locationTrackingService;

    // Guards _ports and _activeSessions; also serializes modem start/stop per service.
    private readonly Lock _stateLock = new();
    private readonly Dictionary<Guid, ActivePortSession> _activeSessions = new();
    private readonly List<PortConfig> _ports;

    // One gate per port so a start and a stop for the same port cannot interleave,
    // and two concurrent starts cannot both pass the "already running" check and
    // leak a modem (audit C-01).
    private readonly Dictionary<Guid, SemaphoreSlim> _sessionGates = [];

    private readonly CancellationTokenSource _disposalCts = new();
    private readonly Channel<QueuedPacket> _packetChannel;
    private readonly Task _packetProcessorTask;
    private readonly Channel<DigipeatCandidate> _digipeatChannel;
    private readonly Task _digipeatProcessorTask;
    private readonly int _digipeatQueueCapacity;
    private readonly IDiagnosticEventSink? _diagnosticSink;

    // Inbound drops are counted per ingress port so a saturated or failing ingest
    // path is visible instead of silently losing packets (audit C-05/C-07).
    private readonly ConcurrentDictionary<Guid, long> _droppedPacketCounts = new();
    private long _totalDroppedPackets;
    private bool _portsKeepAliveActive;

    /// <summary>Default number of queued inbound packets before drops are counted.</summary>
    private const int DefaultPacketQueueCapacity = 1024;

    /// <summary>Default number of queued digipeat candidates before drops are counted.</summary>
    private const int DefaultDigipeatQueueCapacity = 256;

    /// <summary>Maximum time one digipeat transmission may occupy the relay consumer.</summary>
    private static readonly TimeSpan DigipeatTransmitTimeout = TimeSpan.FromSeconds(30);

    /// <summary>Total storage attempts for one inbound packet, including the first.</summary>
    private const int StoreAttemptLimit = 3;

    private sealed record QueuedPacket(Guid PortId, AprsPacket Packet);

    private sealed record DigipeatCandidate(Guid SourcePortId, AprsPacket Packet);

    public PortService(
        IPortRegistry portRegistry,
        IConfigurationService configurationService,
        ILoggerFactory loggerFactory,
        ILogger<PortService> logger,
        IKissStreamFactory kissStreamFactory,
        IPacketStorageService packetStorageService,
        IKeepAliveService keepAliveService,
        IDigipeaterService digipeaterService,
        IAudioStreamService audioStreamService,
        IAudioPlaybackService audioPlaybackService,
        IPttService pttService,
        ILocationTrackingService? locationTrackingService = null,
        IDiagnosticEventSink? diagnosticSink = null,
        int packetQueueCapacity = DefaultPacketQueueCapacity,
        int digipeatQueueCapacity = DefaultDigipeatQueueCapacity)
    {
        ArgumentNullException.ThrowIfNull(portRegistry);

        _portRegistry = portRegistry;
        _configurationService = configurationService;
        _loggerFactory = loggerFactory;
        _logger = logger;
        _kissStreamFactory = kissStreamFactory;
        _packetStorageService = packetStorageService;
        _keepAliveService = keepAliveService;
        _digipeaterService = digipeaterService;
        _audioStreamService = audioStreamService;
        _audioPlaybackService = audioPlaybackService;
        _pttService = pttService;
        _locationTrackingService = locationTrackingService;
        _diagnosticSink = diagnosticSink;

        // Bound the queue so a burst cannot grow it unboundedly; the single consumer
        // also guarantees packets are persisted and published in receive order.
        // Policy: the modem read loop must never block, so a saturated queue drops
        // the newest packet and counts it (see RaisePacketReceived).
        _packetChannel = Channel.CreateBounded<QueuedPacket>(new BoundedChannelOptions(packetQueueCapacity)
        {
            SingleReader = true,
            FullMode = BoundedChannelFullMode.Wait
        });
        _packetProcessorTask = Task.Run(ProcessPacketQueueAsync);

        // Digipeat candidates are relayed by their own consumer so a slow RF
        // transmission never holds up ingest for other ports (audit C-06).
        // The producer checks occupancy before writing, so a full queue drops the
        // incoming candidate and counts it instead of silently discarding another.
        _digipeatQueueCapacity = digipeatQueueCapacity;
        _digipeatChannel = Channel.CreateBounded<DigipeatCandidate>(new BoundedChannelOptions(digipeatQueueCapacity)
        {
            SingleReader = true,
            FullMode = BoundedChannelFullMode.DropNewest
        });
        _digipeatProcessorTask = Task.Run(ProcessDigipeatQueueAsync);

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
                // Hand out copies: a caller must never be able to mutate the
                // service's authoritative state by editing a published snapshot.
                return [.. _ports.Select(port => port.Clone())];
            }
        }
    }

    public event EventHandler? PortsChanged;

    public event EventHandler<PortPacketReceivedEventArgs>? PacketReceived;

    public event EventHandler<PortPacketTransmittedEventArgs>? PacketTransmitted;

    public event EventHandler<PortTransportFaultEventArgs>? TransportFaulted;

    /// <inheritdoc />
    public long GetDroppedPacketCount(Guid portId) =>
        _droppedPacketCounts.TryGetValue(portId, out var count) ? count : 0;

    /// <summary>Total inbound packets dropped across all ports.</summary>
    public long TotalDroppedPackets => Interlocked.Read(ref _totalDroppedPackets);

    /// <inheritdoc />
    public async Task<bool> StartSessionAsync(Guid portId, CancellationToken cancellationToken)
    {
        var port = FindPortById(portId);
        if (port is null)
        {
            return false;
        }

        return await StartModemAsync(port, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task StopSessionAsync(Guid portId, CancellationToken cancellationToken) =>
        StopModemAsync(portId);

    public async Task AddPortAsync(PortConfig port)
    {
        ArgumentNullException.ThrowIfNull(port);

        // Durable-first: never expose a port in memory that was not saved.
        await PersistPortAsync(port, default).ConfigureAwait(false);

        var stored = port.Clone();
        lock (_stateLock)
        {
            _ports.Add(stored);
        }

        PortsChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task UpdatePortAsync(PortConfig updatedPort)
    {
        ArgumentNullException.ThrowIfNull(updatedPort);

        PortConfig? current;
        lock (_stateLock)
        {
            current = _ports.FirstOrDefault(p => p.Id == updatedPort.Id);
        }

        if (current is null)
        {
            return;
        }

        // Durable-first: the persisted row must match the new settings before
        // any session is torn down or any in-memory snapshot is replaced.
        await PersistPortAsync(updatedPort, default).ConfigureAwait(false);

        ActivePortSession? sessionToRestart = null;
        bool startAfterUpdate;

        lock (_stateLock)
        {
            var stored = _ports.FirstOrDefault(p => p.Id == updatedPort.Id);
            if (stored is null)
            {
                return;
            }

            startAfterUpdate = _activeSessions.ContainsKey(stored.Id) || stored.IsEnabled;
            if (_activeSessions.TryGetValue(stored.Id, out var session))
            {
                // Detach the session from the running state before restarting.
                _activeSessions.Remove(stored.Id);
                sessionToRestart = session;
            }

            CopyPortProperties(updatedPort, stored);
        }

        if (sessionToRestart is not null)
        {
            _logger.LogDebug(
                "Restarting running port {PortName} ({PortId}) after settings update.",
                updatedPort.Name,
                updatedPort.Id);
            await StopDetachedSessionAsync(sessionToRestart).ConfigureAwait(false);
        }

        if (startAfterUpdate && updatedPort.IsEnabled)
        {
            await StartModemAsync(updatedPort).ConfigureAwait(false);
        }

        await UpdateForegroundServiceAsync().ConfigureAwait(false);
        PortsChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task RemovePortAsync(Guid id)
    {
        // Stop if running
        await StopModemAsync(id).ConfigureAwait(false);

        // Durable-first: the row must be gone before the port disappears from
        // the in-memory snapshot.
        await DeletePortAsync(id, default).ConfigureAwait(false);

        lock (_stateLock)
        {
            var port = _ports.FirstOrDefault(p => p.Id == id);
            if (port is not null)
            {
                _ports.Remove(port);
            }
        }

        PortsChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task SetPortEnabledAsync(Guid id, bool enabled)
    {
        PortConfig? current;
        lock (_stateLock)
        {
            current = _ports.FirstOrDefault(p => p.Id == id);
        }

        if (current is null)
        {
            return;
        }

        var intentChanged = current.IsEnabled != enabled;

        if (intentChanged)
        {
            // Durable-first: the store is the authority. Only after the write
            // succeeds do we publish the new state to memory, so a failed write
            // can never leave the runtime believing an unpersisted intent is saved.
            var desired = current.WithEnabled(enabled);
            await PersistPortAsync(desired, default).ConfigureAwait(false);

            lock (_stateLock)
            {
                var stored = _ports.FirstOrDefault(p => p.Id == id);
                if (stored is not null)
                {
                    CopyPortProperties(desired, stored);
                }
            }
        }

        // Read the authoritative snapshot after the write so the session starts
        // from exactly what was persisted, never from a stale pre-write copy.
        PortConfig authoritative;
        lock (_stateLock)
        {
            authoritative = _ports.FirstOrDefault(p => p.Id == id)?.Clone() ?? current;
        }

        // Converge the runtime session to the persisted intent. This part is
        // idempotent: asking for an already-enabled port still guarantees it is
        // running, which supervision and startup both rely on.
        if (enabled)
        {
            var started = await StartModemAsync(authoritative).ConfigureAwait(false);
            if (!started)
            {
                // The start failed: roll the installed intent back to disabled,
                // durably first, before reporting the failure to the caller. This
                // always writes, because the failure means the persisted intent
                // cannot be honoured regardless of how it got there.
                await PersistPortAsync(authoritative.WithEnabled(false), default).ConfigureAwait(false);

                lock (_stateLock)
                {
                    var stored = _ports.FirstOrDefault(p => p.Id == id);
                    if (stored is not null)
                    {
                        stored.IsEnabled = false;
                    }
                }

                await UpdateForegroundServiceAsync().ConfigureAwait(false);
                PortsChanged?.Invoke(this, EventArgs.Empty);
                throw new InvalidOperationException($"Failed to start port '{authoritative.Name}'.");
            }
        }
        else
        {
            await StopModemAsync(id).ConfigureAwait(false);
        }

        await UpdateForegroundServiceAsync().ConfigureAwait(false);
        PortsChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task SetPortShowOnMapAsync(Guid id, bool showOnMap)
    {
        PortConfig? current;
        lock (_stateLock)
        {
            current = _ports.FirstOrDefault(p => p.Id == id);
            if (current is null || current.ShowOnMap == showOnMap)
            {
                return;
            }
        }

        // Durable-first, same ordering rule as SetPortEnabledAsync.
        await PersistPortAsync(current.WithShowOnMap(showOnMap), default).ConfigureAwait(false);

        lock (_stateLock)
        {
            var stored = _ports.FirstOrDefault(p => p.Id == id);
            if (stored is not null)
            {
                stored.ShowOnMap = showOnMap;
            }
        }

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

        await session.Modem.SendAsync(packet).ConfigureAwait(false);
        PacketTransmitted?.Invoke(this, new PortPacketTransmittedEventArgs(id, packet));

        // Update foreground service state after auto-starting a stopped port.
        await UpdateForegroundServiceAsync().ConfigureAwait(false);
        _logger.LogDebug("Transmitted APRS packet on port {PortId}, Type={PacketType}.", id, packet.GetType().Name);
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

    private async Task<List<PortConfig>> LoadPortsAsync(CancellationToken cancellationToken = default) =>
        [.. await _portRegistry.LoadAsync(cancellationToken).ConfigureAwait(false)];

    private Task PersistPortAsync(PortConfig port, CancellationToken cancellationToken = default) =>
        _portRegistry.SaveAsync(port, cancellationToken);

    private Task DeletePortAsync(Guid id, CancellationToken cancellationToken = default) =>
        _portRegistry.DeleteAsync(id, cancellationToken);

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

    private SemaphoreSlim GetSessionGate(Guid portId)
    {
        lock (_stateLock)
        {
            if (!_sessionGates.TryGetValue(portId, out var gate))
            {
                gate = new SemaphoreSlim(1, 1);
                _sessionGates[portId] = gate;
            }

            return gate;
        }
    }

    private async Task<bool> StartModemAsync(PortConfig port, CancellationToken cancellationToken = default)
    {
        // The caller's token (session supervision) and disposal both abort a
        // transport that is still opening. BLE/RFCOMM connects can block for
        // many seconds, so the token must reach IKissStreamConnector.ConnectAsync
        // (audit P-18).
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _disposalCts.Token);
        var startToken = linkedCts.Token;

        var gate = GetSessionGate(port.Id);
        await gate.WaitAsync(startToken).ConfigureAwait(false);
        try
        {
            lock (_stateLock)
            {
                if (_activeSessions.ContainsKey(port.Id))
                {
                    _logger.LogDebug("Port {PortId} ({PortName}) is already running.", port.Id, port.Name);
                    return true;
                }
            }

            return await StartModemCoreAsync(port, startToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<bool> StartModemCoreAsync(PortConfig port, CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

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

                        // Resolve runtime-dependent filter directives (drop a
                        // no-op all-types t/, and turn m/ into a fixed r/ range
                        // around the current position) just before connecting.
                        var location = _locationTrackingService?.CurrentLocation;
                        var filter = AprsIsFilter.NormalizeForConnection(
                            aprsIsSettings.Filter,
                            location?.Location.Latitude,
                            location?.Location.Longitude);

                        var modem = new AprsIsModem(
                            aprsIsSettings.Server,
                            aprsIsSettings.ServerPort,
                            fullCallsign,
                            aprsIsSettings.Passcode,
                            filter,
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
                        var stream = await _kissStreamFactory
                            .OpenAsync(kiss, cancellationToken)
                            .ConfigureAwait(false);
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
                case AfskSettings afsk:
                    {
                        var modem = new AfskModem(
                            _audioStreamService,
                            _audioPlaybackService,
                            _pttService,
                            afsk,
                            _loggerFactory);

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

                        _logger.LogInformation("Started AFSK modem for port {PortName} ({PortId}).", port.Name, port.Id);
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
        catch (OperationCanceledException) when (_disposalCts.IsCancellationRequested)
        {
            _logger.LogDebug(
                "Start cancelled while PortService is disposing for port {PortName} ({PortId}).",
                port.Name,
                port.Id);
            return false;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Supervision cancelled the connect (for example while stopping the
            // session); let the caller observe cancellation instead of reporting
            // a failed modem.
            _logger.LogDebug(
                "Start cancelled for port {PortName} ({PortId}).",
                port.Name,
                port.Id);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start modem for port {PortName} ({PortId}).", port.Name, port.Id);
            return false;
        }
    }

    private async Task StopModemAsync(Guid id)
    {
        var gate = GetSessionGate(id);
        await gate.WaitAsync(_disposalCts.Token).ConfigureAwait(false);
        try
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
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// Stops a session that <see cref="UpdatePortAsync"/> already removed from the
    /// active map, under the port's session gate so a concurrent start waits.
    /// </summary>
    private async Task StopDetachedSessionAsync(ActivePortSession session)
    {
        var gate = GetSessionGate(session.PortId);
        await gate.WaitAsync(_disposalCts.Token).ConfigureAwait(false);
        try
        {
            await StopSessionAsync(session).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
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

        if (hasEnabledPorts && !_portsKeepAliveActive)
        {
            _portsKeepAliveActive = true;
            await _keepAliveService.AcquireAsync(KeepAliveReason.Ports).ConfigureAwait(false);
            _logger.LogInformation("Acquired keep-alive for ports");
        }
        else if (!hasEnabledPorts && _portsKeepAliveActive)
        {
            _portsKeepAliveActive = false;
            await _keepAliveService.ReleaseAsync(KeepAliveReason.Ports).ConfigureAwait(false);
            _logger.LogInformation("Released keep-alive for ports");
        }
    }

    private void RaisePacketReceived(Guid portId, AprsPacket packet)
    {
        _logger.LogDebug(
            "Packet received on port {PortId} from {Source}, Type={PacketType}.",
            portId,
            packet.Source,
            packet.GetType().Name);
        if (packet is UnknownPacket)
        {
            _logger.LogDebug(
                "Unknown APRS packet on port {PortId} from {Source}.",
                portId,
                packet.Source);
        }

        // Queue for ordered sequential processing: storage → event, then a
        // separate digipeat consumer. A bounded channel preserves receive order
        // (Id-ordering assumptions in the query service) and prevents unbounded
        // Task.Run fan-out under bursts.
        // TryWrite is intentional: this runs on the modem read loop, which must not
        // block on the database. A full queue therefore drops the newest packet and
        // records it below rather than waiting (audit C-07).
        if (!_packetChannel.Writer.TryWrite(new QueuedPacket(portId, packet)))
        {
            RecordDroppedPacket(portId, packet, "Inbound queue full; newest packet dropped.");
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

    private async Task ProcessDigipeatQueueAsync()
    {
        try
        {
            await foreach (var candidate in _digipeatChannel.Reader.ReadAllAsync(_disposalCts.Token).ConfigureAwait(false))
            {
                try
                {
                    await HandleDigipeatAsync(candidate.SourcePortId, candidate.Packet).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Failed to digipeat packet from {Source} received on port {SourcePortId}.",
                        candidate.Packet.Source,
                        candidate.SourcePortId);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during disposal
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Digipeat queue terminated unexpectedly.");
        }
    }

    private async Task ProcessPacketAsync(Guid portId, AprsPacket packet)
    {
        var stored = await TryStorePacketAsync(portId, packet).ConfigureAwait(false);
        if (!stored)
        {
            // Consumers read the database to render, so publishing an unstored
            // packet would make the UI and the store diverge permanently
            // (audit C-05). Drop it loudly instead.
            RecordDroppedPacket(portId, packet, "Packet could not be persisted.");
            return;
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

        // Handle digipeating on a separate consumer: RF transmit latency must
        // never block ingest of subsequent packets from any port (audit C-06).
        EnqueueDigipeatCandidate(portId, packet);
    }

    /// <summary>
    /// Queues a stored, published packet for the digipeat consumer. Runs on the
    /// ingest consumer, so it never waits for transmit capacity; a full queue
    /// drops the incoming candidate and counts it.
    /// </summary>
    private void EnqueueDigipeatCandidate(Guid portId, AprsPacket packet)
    {
        if (_digipeatChannel.Reader.Count >= _digipeatQueueCapacity)
        {
            RecordDroppedPacket(portId, packet, "Digipeat queue full; candidate dropped.");
            return;
        }

        _digipeatChannel.Writer.TryWrite(new DigipeatCandidate(portId, packet));
    }

    /// <summary>
    /// Persists an inbound packet, retrying transient SQLite contention a bounded
    /// number of times. Returns <see langword="false"/> when the packet must not be
    /// published because it could not be stored.
    /// </summary>
    private async Task<bool> TryStorePacketAsync(Guid portId, AprsPacket packet)
    {
        for (var attempt = 1; attempt <= StoreAttemptLimit; attempt++)
        {
            try
            {
                await _packetStorageService
                    .StorePacketAsync(packet, portId, isOutbound: false, _disposalCts.Token)
                    .ConfigureAwait(false);
                return true;
            }
            catch (OperationCanceledException) when (_disposalCts.IsCancellationRequested)
            {
                // Expected during disposal; nothing to publish.
                return false;
            }
            catch (SqliteException ex) when (attempt < StoreAttemptLimit && IsTransientSqliteFailure(ex))
            {
                _logger.LogWarning(
                    ex,
                    "Transient SQLite failure storing packet from {Source} on port {PortId} (attempt {Attempt}/{Limit}); retrying.",
                    packet.Source,
                    portId,
                    attempt,
                    StoreAttemptLimit);

                if (!await DelayBeforeRetryAsync(attempt).ConfigureAwait(false))
                {
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to store packet from {Source} on port {PortId}; the packet was not published.",
                    packet.Source,
                    portId);
                return false;
            }
        }

        return false;
    }

    private static bool IsTransientSqliteFailure(SqliteException exception) =>
        // SQLITE_BUSY (5) and SQLITE_LOCKED (6) are the two contention codes that
        // clear on their own; anything else is a programming or storage fault.
        exception.SqliteErrorCode is 5 or 6;

    private async Task<bool> DelayBeforeRetryAsync(int attempt)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(50 * attempt), _disposalCts.Token).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private void RecordDroppedPacket(Guid portId, AprsPacket packet, string reason)
    {
        _droppedPacketCounts.AddOrUpdate(portId, 1, static (_, current) => current + 1);
        Interlocked.Increment(ref _totalDroppedPackets);

        _diagnosticSink?.Record(
            "ingest",
            DiagnosticSeverity.Error,
            "Dropped",
            reason,
            portId.ToString("N"));

        _logger.LogWarning(
            "Dropped inbound packet from {Source} on port {PortId}: {Reason}",
            packet.Source,
            portId,
            reason);
    }

    private async Task HandleDigipeatAsync(Guid sourcePortId, AprsPacket packet)
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
            // A wedged modem must not stall the relay consumer forever. The token
            // is linked to disposal and bounds only this transmit; the underlying
            // SendPacketAsync has no cancellation parameter.
            using var transmitCts = CancellationTokenSource.CreateLinkedTokenSource(_disposalCts.Token);
            transmitCts.CancelAfter(DigipeatTransmitTimeout);

            try
            {
                await SendPacketAsync(target.PortId, target.Packet)
                    .WaitAsync(transmitCts.Token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (_disposalCts.IsCancellationRequested)
            {
                // Expected during disposal
                break;
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning(
                    "Digipeat transmit from {Source} to port {TargetPortId} timed out after {TimeoutSeconds} seconds.",
                    packet.Source,
                    target.PortId,
                    DigipeatTransmitTimeout.TotalSeconds);
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

    private void OnModemReceiveError(object? sender, Exception exception)
    {
        Guid? portId = null;
        lock (_stateLock)
        {
            if (sender is not null)
            {
                portId = _activeSessions.Values
                    .FirstOrDefault(session => ReferenceEquals(session.Modem, sender))
                    ?.PortId;
            }
        }

        if (portId is Guid identifiedPortId)
        {
            _logger.LogWarning(exception, "Receive error on port {PortId}.", identifiedPortId);
            TransportFaulted?.Invoke(this, new PortTransportFaultEventArgs(identifiedPortId, exception));
        }
        else
        {
            _logger.LogWarning(exception, "Receive error on an unknown modem.");
        }
    }

    public async ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        _logger.LogInformation("Disposing PortService and stopping all active ports.");

        // Stop packet processing first, then digipeat relay and ports. The CTS is
        // intentionally not disposed: queued callbacks may still reference its
        // token, and disposing it can throw ObjectDisposedException synchronously
        // into modem threads.
        _disposalCts.Cancel();
        _packetChannel.Writer.TryComplete();
        _digipeatChannel.Writer.TryComplete();

        try
        {
            await _packetProcessorTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected during disposal
        }

        try
        {
            await _digipeatProcessorTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected during disposal
        }

        await StopAllPortsAsync().ConfigureAwait(false);

        // Release the port keep-alive reason on disposal.
        if (_portsKeepAliveActive)
        {
            _portsKeepAliveActive = false;
            await _keepAliveService.ReleaseAsync(KeepAliveReason.Ports).ConfigureAwait(false);
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
