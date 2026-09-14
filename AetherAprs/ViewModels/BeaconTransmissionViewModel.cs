// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Models;
using AetherAprs.Services;
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
    public partial string BeaconStatus { get; set; } = "Beacon system ready";

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
                BeaconStatus = $"Next beacon in {decision.SecondsUntilNextBeacon}s ({decision.Reason})";
                return;
            }

            // Get enabled TX ports
            var txPorts = _portService.Ports.Where(p => p.IsEnabled && p.IsTx).ToList();
            if (txPorts.Count == 0)
            {
                BeaconStatus = "No TX ports enabled";
                _logger.LogWarning("Cannot transmit beacon: no TX ports enabled");
                return;
            }

            // Get callsign from configuration
            var callsign = _configurationService.Settings.Aprs.Callsign;
            if (string.IsNullOrEmpty(callsign))
            {
                BeaconStatus = "Callsign not configured";
                _logger.LogWarning("Cannot transmit beacon: callsign not configured");
                return;
            }

            // Send to each TX port with the configured beacon mode
            var sentPortCount = 0;
            foreach (var port in txPorts)
            {
                try
                {
                    _beaconService.SetActiveMode(_portSettingsResolver.GetPortBeaconMode(port));
                    var packet = _beaconService.CreatePositionPacket(
                        currentLocation,
                        _portSettingsResolver.GetPortCallsign(port, callsign),
                        _portSettingsResolver.GetPortSymbolTableCharacter(port),
                        _portSettingsResolver.GetPortSymbolCodeCharacter(port));
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
                BeaconStatus = "Beacon transmission failed";
                return;
            }

            // Reset transmission timer
            _beaconService.ResetTransmissionTimer();

            // Update status
            BeaconStatus = $"✓ Beacon sent ({decision.Reason}) - Speed: {decision.CurrentSpeedKmh:F1}km/h, Course: {decision.CurrentCourseDegrees:F0}°";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error evaluating beacon transmission");
            BeaconStatus = $"Beacon error: {ex.Message}";
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
            BeaconStatus = "No location available";
            return;
        }

        try
        {
            var callsign = _configurationService.Settings.Aprs.Callsign;
            if (string.IsNullOrEmpty(callsign))
            {
                BeaconStatus = "Callsign not configured";
                return;
            }

            var txPorts = _portService.Ports.Where(p => p.IsEnabled && p.IsTx).ToList();
            if (txPorts.Count == 0)
            {
                BeaconStatus = "No TX ports enabled";
                return;
            }

            var sentPortCount = 0;
            foreach (var port in txPorts)
            {
                try
                {
                    _beaconService.SetActiveMode(_portSettingsResolver.GetPortBeaconMode(port));
                    var packet = _beaconService.CreatePositionPacket(
                        userLocation,
                        _portSettingsResolver.GetPortCallsign(port, callsign),
                        _portSettingsResolver.GetPortSymbolTableCharacter(port),
                        _portSettingsResolver.GetPortSymbolCodeCharacter(port));
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
                ? "✓ Manual beacon sent"
                : "Manual beacon transmission failed";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending manual beacon");
            BeaconStatus = $"Error: {ex.Message}";
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

        var sentPortNames = new System.Collections.Generic.List<string>();
        foreach (var port in portsJustEnabled)
        {
            try
            {
                _beaconService.SetActiveMode(_portSettingsResolver.GetPortBeaconMode(port));
                var packet = _beaconService.CreatePositionPacket(
                    userLocation,
                    _portSettingsResolver.GetPortCallsign(port, callsign),
                    _portSettingsResolver.GetPortSymbolTableCharacter(port),
                    _portSettingsResolver.GetPortSymbolCodeCharacter(port));
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
            BeaconStatus = $"✓ Initial beacon sent on port activation ({portNames})";
        }
    }
}
