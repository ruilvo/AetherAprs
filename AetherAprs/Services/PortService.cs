// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AetherAprs.Configuration;
using AetherAprs.Helpers;
using AetherAprs.Models.Aprs;
using AetherAprs.Modems.Aprs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AetherAprs.Services;

public class PortService : IPortService
{
    private readonly IConfigurationService _configurationService;
    private readonly ILogger<PortService> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly Dictionary<Guid, AprsIsModem> _activeModems = new();

    public PortService(
        IConfigurationService configurationService,
        ILogger<PortService> logger,
        IServiceProvider serviceProvider)
    {
        _configurationService = configurationService;
        _logger = logger;
        _serviceProvider = serviceProvider;
    }

    public IReadOnlyList<PortConfig> Ports => _configurationService.Settings.Ports;

    public event EventHandler? PortsChanged;

    public async Task AddPortAsync(PortConfig port)
    {
        _configurationService.Settings.Ports.Add(port);
        await _configurationService.SaveSettingsAsync();
        PortsChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task UpdatePortAsync(PortConfig updatedPort)
    {
        var port = _configurationService.Settings.Ports.FirstOrDefault(p => p.Id == updatedPort.Id);
        if (port is not null)
        {
            port.Name = updatedPort.Name;
            port.IsRx = updatedPort.IsRx;
            port.IsTx = updatedPort.IsTx;
            port.Server = updatedPort.Server;
            port.ServerPort = updatedPort.ServerPort;
            port.Passcode = updatedPort.Passcode;
            port.Filter = updatedPort.Filter;
            port.Ssid = updatedPort.Ssid;
            port.DynamicBeaconMode = updatedPort.DynamicBeaconMode;
            await _configurationService.SaveSettingsAsync();
            PortsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public async Task RemovePortAsync(Guid id)
    {
        // Stop if running
        await StopModemAsync(id);

        var port = _configurationService.Settings.Ports.FirstOrDefault(p => p.Id == id);
        if (port is not null)
        {
            _configurationService.Settings.Ports.Remove(port);
            await _configurationService.SaveSettingsAsync();
            PortsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public async Task SetPortEnabledAsync(Guid id, bool enabled)
    {
        var port = _configurationService.Settings.Ports.FirstOrDefault(p => p.Id == id);
        if (port is null)
        {
            return;
        }

        port.IsEnabled = enabled;
        await _configurationService.SaveSettingsAsync();

        if (enabled)
        {
            await StartModemAsync(port);
        }
        else
        {
            await StopModemAsync(id);
        }

        PortsChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task SendPacketAsync(Guid id, AprsPacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);

        if (!_activeModems.TryGetValue(id, out var modem))
        {
            throw new InvalidOperationException($"Port {id} is not running.");
        }

        var portName = Ports.FirstOrDefault(port => port.Id == id)?.Name ?? id.ToString();
        var infoField = AprsSerializer.FormatInfoField(packet);
        var rawPacket = $"{packet.Source}>{packet.Destination},TCPIP*:{infoField}";

        await modem.SendAsync(packet);

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
                packet.Type,
                rawPacket);
        }
    }

    public async Task StartAllEnabledPortsAsync()
    {
        foreach (var port in _configurationService.Settings.Ports)
        {
            if (port.IsEnabled)
            {
                await StartModemAsync(port);
            }
        }
    }

    public async Task StopAllPortsAsync()
    {
        foreach (var id in _activeModems.Keys.ToList())
        {
            await StopModemAsync(id);
        }
    }

    private async Task StartModemAsync(PortConfig port)
    {
        if (_activeModems.ContainsKey(port.Id))
        {
            _logger.LogDebug("Port {PortId} ({PortName}) is already running.", port.Id, port.Name);
            return;
        }

        try
        {
            AprsIsModem modem;

            if (port.Type == PortType.AprsIs)
            {
                var callsign = _configurationService.Settings.Aprs.Callsign;
                var ssid = port.Ssid ?? _configurationService.Settings.Aprs.DefaultSsid;
                var fullCallsign = new Callsign(callsign, ssid);

                modem = new AprsIsModem(
                    port.Server!,
                    port.ServerPort,
                    fullCallsign,
                    port.Passcode!,
                    port.Filter ?? string.Empty,
                    _serviceProvider.GetRequiredService<ILogger<AprsIsModem>>());

                modem.PacketReceived += OnModemPacketReceived;
                modem.ReceiveError += OnModemReceiveError;
                modem.Start();

                _activeModems[port.Id] = modem;
                _logger.LogInformation("Started modem for port {PortName} ({PortId}).", port.Name, port.Id);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start modem for port {PortName} ({PortId}).", port.Name, port.Id);
        }
    }

    private async Task StopModemAsync(Guid id)
    {
        if (_activeModems.TryGetValue(id, out var modem))
        {
            modem.PacketReceived -= OnModemPacketReceived;
            modem.ReceiveError -= OnModemReceiveError;
            await modem.StopAsync();
            await modem.DisposeAsync();
            _activeModems.Remove(id);
            _logger.LogInformation("Stopped modem for port {PortId}.", id);
        }
    }

    private void OnModemPacketReceived(object? sender, AprsPacket packet)
    {
        _logger.LogInformation("Packet received from {Source}: {Raw}", packet.Source, packet.Raw);
    }

    private void OnModemReceiveError(object? sender, Exception exception)
    {
        _logger.LogWarning(exception, "Receive error on modem.");
    }
}
