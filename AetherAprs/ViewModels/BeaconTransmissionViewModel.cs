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
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace AetherAprs.ViewModels;

/// <summary>
/// ViewModel responsible for beacon transmission logic and status.
/// </summary>
public partial class BeaconTransmissionViewModel : ViewModelBase, IDisposable
{
    private readonly IBeaconService _beaconService;
    private readonly IPortService _portService;
    private readonly IConfigurationService _configurationService;
    private readonly IAprsPortSettingsResolver _portSettingsResolver;
    private readonly IPacketFactory _packetFactory;
    private readonly ILogger<BeaconTransmissionViewModel> _logger;

    public BeaconTransmissionViewModel(
        IBeaconService beaconService,
        IPortService portService,
        IConfigurationService configurationService,
        IAprsPortSettingsResolver portSettingsResolver,
        IPacketFactory packetFactory,
        ILogger<BeaconTransmissionViewModel> logger)
    {
        _beaconService = beaconService;
        _portService = portService;
        _configurationService = configurationService;
        _portSettingsResolver = portSettingsResolver;
        _packetFactory = packetFactory;
        _logger = logger;

        // Subscribe to beacon requested event
        _beaconService.BeaconRequested += OnBeaconRequested;
    }

    public void Dispose()
    {
        _beaconService.BeaconRequested -= OnBeaconRequested;
    }

    [ObservableProperty]
    public partial BeaconTransmitDecision? LastBeaconDecision { get; set; }

    [ObservableProperty]
    public partial string BeaconStatus { get; set; } = Strings.Get("BeaconSystemReady");

    /// <summary>
    /// Processes a location update through the beacon service.
    /// The service will raise BeaconRequested event if transmission is needed.
    /// </summary>
    public void ProcessLocationUpdate(LocationData currentLocation)
    {
        _beaconService.ProcessLocationUpdate(currentLocation);

        // Update UI with current decision
        var decision = _beaconService.CurrentDecision;
        LastBeaconDecision = decision;

        if (decision is not null && !decision.ShouldTransmit)
        {
            BeaconStatus = Strings.Format("NextBeaconIn", decision.SecondsUntilNextBeacon, decision.Reason.GetLocalizedString());
        }
    }

    /// <summary>
    /// Event handler for when the beacon service requests transmission.
    /// </summary>
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

    /// <summary>
    /// Transmits a beacon on all enabled TX ports.
    /// </summary>
    private async Task TransmitBeaconAsync(LocationData location, BeaconTransmitDecision decision)
    {
        // Get enabled TX ports
        var txPorts = _portService.Ports.Where(p => p.IsEnabled && p.IsTx).ToList();
        if (txPorts.Count == 0)
        {
            BeaconStatus = Strings.Get("NoTxPortsEnabled");
            _logger.LogWarning("Cannot transmit beacon: no TX ports enabled");
            return;
        }

        // Get callsign from configuration
        var callsign = _configurationService.Settings.Aprs.Callsign;
        if (string.IsNullOrEmpty(callsign))
        {
            BeaconStatus = Strings.Get("CallsignNotConfigured");
            _logger.LogWarning("Cannot transmit beacon: callsign not configured");
            return;
        }

        // Create position packet
        var packet = CreateBeaconPacket(location, callsign);

        // Send to each TX port
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

        // Update status
        BeaconStatus = Strings.Format("BeaconSentStatus", decision.Reason.GetLocalizedString(), decision.CurrentSpeedKmh, decision.CurrentCourseDegrees);
    }

    /// <summary>
    /// Sends a manual beacon immediately on all enabled TX ports.
    /// </summary>
    [RelayCommand]
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

            // Create position packet using current global beacon mode
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

    /// <summary>
    /// Sends an initial beacon on newly enabled ports.
    /// </summary>
    public async Task SendInitialBeaconOnPortActivationAsync(LocationData? userLocation, System.Collections.Generic.IEnumerable<Configuration.PortConfig> portsJustEnabled)
    {
        if (userLocation == null || !portsJustEnabled.Any())
            return;

        var callsign = _configurationService.Settings.Aprs.Callsign;
        if (string.IsNullOrEmpty(callsign))
            return;

        // Create position packet using current global beacon mode
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

    /// <summary>
    /// Creates a beacon packet for the given location and callsign.
    /// </summary>
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
}
