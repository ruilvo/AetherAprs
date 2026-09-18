// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Models;
using AetherAprs.Services;
using AetherAprs.Localization;
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
public partial class BeaconTransmissionViewModel : ViewModelBase
{
    private readonly IBeaconService _beaconService;
    private readonly IPortService _portService;
    private readonly IConfigurationService _configurationService;
    private readonly IAprsPortSettingsResolver _portSettingsResolver;
    private readonly ILogger<BeaconTransmissionViewModel> _logger;

    [ObservableProperty]
    public partial BeaconTransmitDecision? LastBeaconDecision { get; set; }

    [ObservableProperty]
    public partial string BeaconStatus { get; set; } = Strings.Get("BeaconSystemReady");

    public BeaconTransmissionViewModel(
        IBeaconService beaconService,
        IPortService portService,
        IConfigurationService configurationService,
        IAprsPortSettingsResolver portSettingsResolver,
        ILogger<BeaconTransmissionViewModel> logger)
    {
        _beaconService = beaconService;
        _portService = portService;
        _configurationService = configurationService;
        _portSettingsResolver = portSettingsResolver;
        _logger = logger;
    }

    /// <summary>
    /// Evaluates whether to transmit a beacon based on the current location,
    /// and transmits if appropriate.
    /// </summary>
    public async Task EvaluateAndTransmitBeaconAsync(LocationData currentLocation, LocationData? previousLocation)
    {
        try
        {
            // Evaluate whether to transmit
            var decision = _beaconService.EvaluateLocationUpdate(currentLocation, previousLocation);
            LastBeaconDecision = decision;

            if (!decision.ShouldTransmit)
            {
                BeaconStatus = Strings.Format("NextBeaconIn", decision.SecondsUntilNextBeacon, decision.Reason);
                return;
            }

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

            var sourceCallsign = _portSettingsResolver.GetCallsign(callsign);
            var symbolTable = _portSettingsResolver.GetSymbolTableCharacter();
            var symbolCode = _portSettingsResolver.GetSymbolCodeCharacter();

            // Send to each TX port with the configured beacon mode
            var sentPortCount = 0;
            foreach (var port in txPorts)
            {
                try
                {
                    _beaconService.SetActiveMode(_portSettingsResolver.GetPortBeaconMode(port));
                    var packet = _beaconService.CreatePositionPacket(
                        currentLocation,
                        sourceCallsign,
                        symbolTable,
                        symbolCode);
                    await _portService.SendPacketAsync(port.Id, packet);
                    sentPortCount++;
                    _logger.LogInformation(
                        "Beacon transmitted on port {PortName}: {Lat}, {Lon} (Speed: {Speed:F1}km/h, Course: {Course:F0}°)",
                        port.Name,
                        currentLocation.Latitude,
                        currentLocation.Longitude,
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

            // Reset transmission timer
            _beaconService.ResetTransmissionTimer();

            // Update status
            BeaconStatus = Strings.Format("BeaconSentStatus", decision.Reason, decision.CurrentSpeedKmh, decision.CurrentCourseDegrees);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error evaluating beacon transmission");
            BeaconStatus = Strings.Format("BeaconError", ex.Message);
        }
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

            var sourceCallsign = _portSettingsResolver.GetCallsign(callsign);
            var symbolTable = _portSettingsResolver.GetSymbolTableCharacter();
            var symbolCode = _portSettingsResolver.GetSymbolCodeCharacter();

            var sentPortCount = 0;
            foreach (var port in txPorts)
            {
                try
                {
                    _beaconService.SetActiveMode(_portSettingsResolver.GetPortBeaconMode(port));
                    var packet = _beaconService.CreatePositionPacket(
                        userLocation,
                        sourceCallsign,
                        symbolTable,
                        symbolCode);
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

        var sourceCallsign = _portSettingsResolver.GetCallsign(callsign);
        var symbolTable = _portSettingsResolver.GetSymbolTableCharacter();
        var symbolCode = _portSettingsResolver.GetSymbolCodeCharacter();

        var sentPortNames = new System.Collections.Generic.List<string>();
        foreach (var port in portsJustEnabled)
        {
            try
            {
                _beaconService.SetActiveMode(_portSettingsResolver.GetPortBeaconMode(port));
                var packet = _beaconService.CreatePositionPacket(
                    userLocation,
                    sourceCallsign,
                    symbolTable,
                    symbolCode);
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
            _beaconService.ResetTransmissionTimer();
            var portNames = string.Join(", ", sentPortNames);
            BeaconStatus = Strings.Format("InitialBeaconSentOnPortActivation", portNames);
        }
    }
}
