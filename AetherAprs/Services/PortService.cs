// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AetherAprs.Configuration;
using AetherAprs.Data;
using AetherAprs.Helpers;
using AetherAprs.Models.Aprs;
using AetherAprs.Modems.Aprs;
using AetherAprs.Modems.Kiss;
using AetherAprs.Transports.Kiss;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AetherAprs.Services;

public class PortService : IPortService, IAsyncDisposable
{
    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;
    private readonly IConfigurationService _configurationService;
    private readonly ILogger<PortService> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly IKissStreamFactory _kissStreamFactory;
    private readonly Dictionary<Guid, ActivePortSession> _activeSessions = new();
    private readonly List<PortConfig> _ports;

    public PortService(
        IDbContextFactory<AppDbContext> dbContextFactory,
        IConfigurationService configurationService,
        ILogger<PortService> logger,
        IServiceProvider serviceProvider,
        IKissStreamFactory kissStreamFactory)
    {
        _dbContextFactory = dbContextFactory;
        _configurationService = configurationService;
        _logger = logger;
        _serviceProvider = serviceProvider;
        _kissStreamFactory = kissStreamFactory;
        _ports = LoadPorts();
    }

    public IReadOnlyList<PortConfig> Ports => _ports;

    public event EventHandler? PortsChanged;

    public event EventHandler<PortPacketReceivedEventArgs>? PacketReceived;

    public async Task AddPortAsync(PortConfig port)
    {
        _ports.Add(port);
        await PersistPortAsync(port).ConfigureAwait(false);
        PortsChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task UpdatePortAsync(PortConfig updatedPort)
    {
        var port = FindPortById(updatedPort.Id);
        if (port is not null)
        {
            var wasRunning = _activeSessions.ContainsKey(updatedPort.Id);
            CopyPortProperties(updatedPort, port);
            await PersistPortAsync(port).ConfigureAwait(false);

            if (wasRunning || port.IsEnabled)
            {
                if (wasRunning)
                {
                    _logger.LogDebug(
                        "Restarting running port {PortName} ({PortId}) after settings update.",
                        port.Name,
                        port.Id);
                }

                await StopModemAsync(port.Id);
                if (port.IsEnabled)
                {
                    await StartModemAsync(port);
                }
            }

            PortsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public async Task RemovePortAsync(Guid id)
    {
        // Stop if running
        await StopModemAsync(id);

        var port = FindPortById(id);
        if (port is not null)
        {
            _ports.Remove(port);
            await DeletePortAsync(id).ConfigureAwait(false);
            PortsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public async Task SetPortEnabledAsync(Guid id, bool enabled)
    {
        var port = FindPortById(id);
        if (port is null)
        {
            return;
        }

        port.IsEnabled = enabled;
        await PersistPortAsync(port).ConfigureAwait(false);

        if (enabled)
        {
            var started = await StartModemAsync(port);
            if (!started)
            {
                port.IsEnabled = false;
                await PersistPortAsync(port).ConfigureAwait(false);
                PortsChanged?.Invoke(this, EventArgs.Empty);
                throw new InvalidOperationException($"Failed to start port '{port.Name}'.");
            }
        }
        else
        {
            await StopModemAsync(id);
        }

        PortsChanged?.Invoke(this, EventArgs.Empty);
    }


    public async Task SetPortShowOnMapAsync(Guid id, bool showOnMap)
    {
        var port = FindPortById(id);
        if (port is null || port.ShowOnMap == showOnMap)
        {
            return;
        }

        port.ShowOnMap = showOnMap;
        await PersistPortAsync(port).ConfigureAwait(false);
        PortsChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task SendPacketAsync(Guid id, AprsPacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);

        if (!_activeSessions.TryGetValue(id, out var session))
        {
            var portToStart = FindPortById(id);
            if (portToStart is null || !portToStart.IsEnabled)
            {
                throw new InvalidOperationException($"Port {id} is not running.");
            }

            if (!await StartModemAsync(portToStart) ||
                !_activeSessions.TryGetValue(id, out session))
            {
                throw new InvalidOperationException($"Port {id} is not running.");
            }
        }

        var port = FindPortById(id);
        var portName = port?.Name ?? id.ToString();
        var infoField = AprsInfoFieldSerializer.FormatInfoField(packet);
        var rawPacket = port?.TypeSettings is AprsIsSettings
            ? $"{packet.Source}>{packet.Destination},TCPIP*:{infoField}"
            : $"{packet.Source}>{packet.Destination}:{infoField}";

        await session.Modem.SendAsync(packet);

        if (packet is PositionPacket position)
        {
            _logger.LogInformation(
                "Transmitted APRS position on port {PortName} ({PortId}): Latitude={Latitude:F5}, Longitude={Longitude:F5}, Altitude={Altitude}, Course={Course}, Speed={Speed}, Comment={Comment}, RawPacket={RawPacket}",
                portName,
                id,
                position.Latitude,
                position.Longitude,
                position.Altitude,
                position.Course,
                position.Speed,
                position.Comment,
                rawPacket);
        }
        else
        {
            _logger.LogInformation(
                "Transmitted APRS packet on port {PortName} ({PortId}), Type={PacketType}, RawPacket={RawPacket}",
                portName,
                id,
                packet.GetType().Name,
                rawPacket);
        }
    }

    public async Task StartAllEnabledPortsAsync()
    {
        foreach (var port in _ports)
        {
            if (port.IsEnabled)
            {
                await StartModemAsync(port);
            }
        }
    }

    public async Task StopAllPortsAsync()
    {
        // Create a copy to avoid collection modification during iteration
        var idsToStop = _activeSessions.Keys.ToArray();
        foreach (var id in idsToStop)
        {
            await StopModemAsync(id);
        }
    }

    private PortConfig? FindPortById(Guid id)
    {
        return _ports.FirstOrDefault(p => p.Id == id);
    }

    private List<PortConfig> LoadPorts()
    {
        using var db = _dbContextFactory.CreateDbContext();
        return [.. db.Ports.AsNoTracking().AsEnumerable().Select(PortRecordMapper.ToConfig)];
    }

    private async Task PersistPortAsync(PortConfig port)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync().ConfigureAwait(false);
        var existing = await db.Ports.FindAsync(port.Id).ConfigureAwait(false);
        if (existing is null)
        {
            db.Ports.Add(PortRecordMapper.ToRecord(port));
        }
        else
        {
            PortRecordMapper.CopyTo(existing, port);
        }

        await db.SaveChangesAsync().ConfigureAwait(false);
    }

    private async Task DeletePortAsync(Guid id)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync().ConfigureAwait(false);
        var existing = await db.Ports.FindAsync(id).ConfigureAwait(false);
        if (existing is not null)
        {
            db.Ports.Remove(existing);
            await db.SaveChangesAsync().ConfigureAwait(false);
        }
    }

    private static void CopyPortProperties(PortConfig source, PortConfig target)
    {
        target.Name = source.Name;
        target.IsEnabled = source.IsEnabled;
        target.IsRx = source.IsRx;
        target.IsTx = source.IsTx;
        target.ShowOnMap = source.ShowOnMap;
        target.TypeSettings = source.TypeSettings;
    }

    private async Task<bool> StartModemAsync(PortConfig port)
    {
        if (_activeSessions.ContainsKey(port.Id))
        {
            _logger.LogDebug("Port {PortId} ({PortName}) is already running.", port.Id, port.Name);
            return true;
        }

        try
        {
            switch (port.TypeSettings)
            {
                case AprsIsSettings aprsIsSettings:
                {
                    var callsign = _configurationService.Settings.Aprs.Callsign;
                    var ssid = _configurationService.Settings.Aprs.DefaultSsid;
                    var fullCallsign = new Callsign(callsign, ssid > 0 ? ssid : null);

                    var modem = new AprsIsModem(
                        aprsIsSettings.Server,
                        aprsIsSettings.ServerPort,
                        fullCallsign,
                        aprsIsSettings.Passcode,
                        aprsIsSettings.Filter,
                        _serviceProvider.GetRequiredService<ILogger<AprsIsModem>>());

                    EventHandler<AprsPacket> packetHandler = (_, packet) => RaisePacketReceived(port.Id, packet);
                    modem.PacketReceived += packetHandler;
                    modem.ReceiveError += OnModemReceiveError;
                    modem.Start();

                    _activeSessions[port.Id] = new ActivePortSession
                    {
                        Modem = modem,
                        PacketHandler = packetHandler
                    };
                    _logger.LogInformation("Started modem for port {PortName} ({PortId}).", port.Name, port.Id);
                    break;
                }
                case KissSettings kiss:
                {
                    var stream = await _kissStreamFactory.OpenAsync(kiss).ConfigureAwait(false);
                    var loggerFactory = _serviceProvider.GetRequiredService<ILoggerFactory>();
                    var kissModem = new KissModem(stream, loggerFactory);
                    var modem = new AprsRfModem(kissModem, loggerFactory);

                    EventHandler<AprsPacket> packetHandler = (_, packet) => RaisePacketReceived(port.Id, packet);
                    modem.PacketReceived += packetHandler;
                    modem.ReceiveError += OnModemReceiveError;
                    modem.Start();

                    _activeSessions[port.Id] = new ActivePortSession
                    {
                        Modem = modem,
                        KissModem = kissModem,
                        PacketHandler = packetHandler
                    };
                    _logger.LogInformation("Started KISS modem for port {PortName} ({PortId}).", port.Name, port.Id);
                    break;
                }
            }

            if (!_activeSessions.ContainsKey(port.Id))
            {
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
        if (!_activeSessions.TryGetValue(id, out var session))
        {
            return;
        }

        session.Modem.PacketReceived -= session.PacketHandler;
        session.Modem.ReceiveError -= OnModemReceiveError;
        await session.Modem.StopAsync();

        if (session.Modem is IAsyncDisposable modemDisposable)
        {
            await modemDisposable.DisposeAsync();
        }

        if (session.KissModem is not null)
        {
            await session.KissModem.DisposeAsync();
        }

        _activeSessions.Remove(id);
        _logger.LogInformation("Stopped modem for port {PortId}.", id);
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
        PacketReceived?.Invoke(this, new PortPacketReceivedEventArgs
        {
            PortId = portId,
            Packet = packet
        });
    }

    private void OnModemReceiveError(object? sender, Exception exception)
    {
        _logger.LogWarning(exception, "Receive error on modem.");
    }

    public async ValueTask DisposeAsync()
    {
        _logger.LogInformation("Disposing PortService and stopping all active ports.");
        await StopAllPortsAsync();
    }

    private sealed class ActivePortSession : IAsyncDisposable
    {
        public required IAprsModem Modem { get; init; }

        /// <summary>
        /// Owned KISS modem when the session wraps an <see cref="AprsRfModem"/>.
        /// </summary>
        public KissModem? KissModem { get; init; }

        public required EventHandler<AprsPacket> PacketHandler { get; init; }

        public async ValueTask DisposeAsync()
        {
            Modem.PacketReceived -= PacketHandler;
            await Modem.StopAsync();

            if (Modem is IAsyncDisposable modemDisposable)
            {
                await modemDisposable.DisposeAsync();
            }

            if (KissModem is not null)
            {
                await KissModem.DisposeAsync();
            }
        }
    }
}
