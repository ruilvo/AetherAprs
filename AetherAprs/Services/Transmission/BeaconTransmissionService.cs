// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Factories.Packets;
using AetherAprs.Localization;
using AetherAprs.Models;
using AetherAprs.Models.Aprs.Packets;
using AetherAprs.Services.Beaconing;
using AetherAprs.Services.Configuration;
using AetherAprs.Services.Ports;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace AetherAprs.Services.Transmission;

/// <summary>
/// Service responsible for beacon transmission logic.
/// </summary>
public interface IBeaconTransmissionService : IDisposable
{
    /// <summary>
    /// Gets the last beacon decision made by the service.
    /// </summary>
    BeaconTransmitDecision? LastBeaconDecision { get; }

    /// <summary>
    /// Gets the current beacon status message.
    /// </summary>
    string BeaconStatus { get; }

    /// <summary>
    /// Event raised when beacon status changes.
    /// </summary>
    event EventHandler<string>? BeaconStatusChanged;

    /// <summary>
    /// Processes a location update through the beacon service.
    /// The service will transmit if conditions are met.
    /// </summary>
    void ProcessLocationUpdate(LocationData currentLocation);

    /// <summary>
    /// Sends a manual beacon immediately on all enabled TX ports.
    /// </summary>
    Task SendManualBeaconAsync(LocationData? userLocation);

    /// <summary>
    /// Sends an initial beacon on newly enabled ports.
    /// </summary>
    Task SendInitialBeaconOnPortActivationAsync(LocationData? userLocation, System.Collections.Generic.IEnumerable<AetherAprs.Configuration.PortConfig> portsJustEnabled);
}

/// <summary>
/// Implementation of beacon transmission service.
/// </summary>
public sealed class BeaconTransmissionService : IBeaconTransmissionService
{
    private readonly IBeaconService _beaconService;
    private readonly IPortService _portService;
    private readonly IConfigurationService _configurationService;
    private readonly IAprsPortSettingsResolver _portSettingsResolver;
    private readonly IPacketFactory _packetFactory;
    private readonly ILogger<BeaconTransmissionService> _logger;
    private string _beaconStatus = Strings.Get("BeaconSystemReady");
    private bool _disposed;

    public BeaconTransmitDecision? LastBeaconDecision { get; private set; }
    
    public string BeaconStatus
    {
        get => _beaconStatus;
        private set
        {
            if (_beaconStatus != value)
            {
                _beaconStatus = value;
                BeaconStatusChanged?.Invoke(this, value);
            }
        }
    }

    public event EventHandler<string>? BeaconStatusChanged;

    public BeaconTransmissionService(
        IBeaconService beaconService,
        IPortService portService,
        IConfigurationService configurationService,
        IAprsPortSettingsResolver portSettingsResolver,
        IPacketFactory packetFactory,
        ILogger<BeaconTransmissionService> logger)
    {
        _beaconService = beaconService;
        _portService = portService;
        _configurationService = configurationService;
        _portSettingsResolver = portSettingsResolver;
        _packetFactory = packetFactory;
        _logger = logger;

        _beaconService.BeaconRequested += OnBeaconRequested;
    }

    public void ProcessLocationUpdate(LocationData currentLocation)
    {
        _beaconService.ProcessLocationUpdate(currentLocation);

        var decision = _beaconService.CurrentDecision;
        LastBeaconDecision = decision;

        if (decision is not null && !decision.ShouldTransmit)
        {
            BeaconStatus = Strings.Format("NextBeaconIn", decision.SecondsUntilNextBeacon, decision.Reason.GetLocalizedString());
        }
    }

    private async void OnBeaconRequested(object? sender, BeaconRequestedEventArgs e)
    {
        try
        {
            await TransmitBeaconAsync(e.Location, e.Decision);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling beacon request");
            BeaconStatus = Strings.Format("BeaconError", ex.Message);
        }
    }

    private async Task TransmitBeaconAsync(LocationData location, BeaconTransmitDecision decision)
    {
        var txPorts = _portService.Ports.Where(p => p.IsEnabled && p.IsTx).ToList();
        if (txPorts.Count == 0)
        {
            BeaconStatus = Strings.Get("NoTxPortsEnabled");
            _logger.LogWarning("Cannot transmit beacon: no TX ports enabled");
            return;
        }

        var callsign = _configurationService.Settings.Aprs.Callsign;
        if (string.IsNullOrEmpty(callsign))
        {
            BeaconStatus = Strings.Get("CallsignNotConfigured");
            _logger.LogWarning("Cannot transmit beacon: callsign not configured");
            return;
        }

        var packet = CreateBeaconPacket(location, callsign);

        var sentPortCount = 0;
        foreach (var port in txPorts)
        {
            try
            {
                await _portService.SendPacketAsync(port.Id, packet);
                sentPortCount++;
                _logger.LogInformation(
                    "Beacon transmitted on port {PortName}: {Lat}, {Lon} (Speed: {Speed:F1}km/h, Course: {Course:F0}°)",
                    port.Name,
                    location.Location.Latitude,
                    location.Location.Longitude,
                    decision.CurrentSpeedKmh ?? 0,
                    decision.CurrentCourseDegrees ?? 0);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error transmitting beacon on port {PortName}", port.Name);
            }
        }

        if (sentPortCount == 0)
        {
            BeaconStatus = Strings.Get("BeaconTransmissionFailed");
            return;
        }

        BeaconStatus = Strings.Format("BeaconSentStatus", decision.Reason.GetLocalizedString(), decision.CurrentSpeedKmh, decision.CurrentCourseDegrees);
    }

    public async Task SendManualBeaconAsync(LocationData? userLocation)
    {
        if (userLocation == null)
        {
            BeaconStatus = Strings.Get("NoLocationAvailable");
            return;
        }

        try
        {
            var callsign = _configurationService.Settings.Aprs.Callsign;
            if (string.IsNullOrEmpty(callsign))
            {
                BeaconStatus = Strings.Get("CallsignNotConfigured");
                return;
            }

            var txPorts = _portService.Ports.Where(p => p.IsEnabled && p.IsTx).ToList();
            if (txPorts.Count == 0)
            {
                BeaconStatus = Strings.Get("NoTxPortsEnabled");
                return;
            }

            var packet = CreateBeaconPacket(userLocation, callsign);

            var sentPortCount = 0;
            foreach (var port in txPorts)
            {
                try
                {
                    await _portService.SendPacketAsync(port.Id, packet);
                    sentPortCount++;
                    _logger.LogInformation("Manual beacon sent on port {PortName}", port.Name);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error sending manual beacon on port {PortName}", port.Name);
                }
            }

            BeaconStatus = sentPortCount > 0
                ? Strings.Get("ManualBeaconSent")
                : Strings.Get("ManualBeaconTransmissionFailed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending manual beacon");
            BeaconStatus = Strings.Format("ErrorPrefix", ex.Message);
        }
    }

    public async Task SendInitialBeaconOnPortActivationAsync(LocationData? userLocation, System.Collections.Generic.IEnumerable<AetherAprs.Configuration.PortConfig> portsJustEnabled)
    {
        if (userLocation == null || !portsJustEnabled.Any())
            return;

        var callsign = _configurationService.Settings.Aprs.Callsign;
        if (string.IsNullOrEmpty(callsign))
            return;

        var packet = CreateBeaconPacket(userLocation, callsign);

        var sentPortNames = new System.Collections.Generic.List<string>();
        foreach (var port in portsJustEnabled)
        {
            try
            {
                await _portService.SendPacketAsync(port.Id, packet);
                sentPortNames.Add(port.Name);
                _logger.LogInformation("Initial beacon sent due to port activation: {Port}", port.Name);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending initial beacon on port activation for {PortName}", port.Name);
            }
        }

        if (sentPortNames.Count > 0)
        {
            var portNames = string.Join(", ", sentPortNames);
            BeaconStatus = Strings.Format("InitialBeaconSentOnPortActivation", portNames);
        }
    }

    private PositionPacket CreateBeaconPacket(LocationData location, string callsign)
    {
        var sourceCallsign = _portSettingsResolver.GetCallsign(callsign);
        var symbolTable = _portSettingsResolver.GetSymbolTable();
        var symbolCode = _portSettingsResolver.GetSymbolCode();
        var course = _beaconService.LastCourseDegrees;
        var comment = _beaconService.CurrentConfiguration.BeaconComment;

        return _packetFactory.CreatePositionPacket(
            location,
            sourceCallsign,
            symbolTable,
            symbolCode,
            course,
            comment);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _beaconService.BeaconRequested -= OnBeaconRequested;
    }
}
