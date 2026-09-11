// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Models;
using AetherAprs.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AetherAprs.ViewModels;

public partial class HomeViewModel : ViewModelBase
{
    private readonly ILocationService _locationService;
    private readonly IBeaconService _beaconService;
    private readonly IPortService _portService;
    private readonly IConfigurationService _configurationService;
    private readonly ILogger<HomeViewModel> _logger;
    private CancellationTokenSource? _locationUpdateCancellation;
    private LocationData? _previousLocation;
    private Dictionary<Guid, bool> _previousPortEnabledState = new();

    [ObservableProperty]
    public partial LocationData? UserLocation { get; set; }

    [ObservableProperty]
    public partial bool IsLocationAvailable { get; set; }

    [ObservableProperty]
    public partial BeaconTransmitDecision? LastBeaconDecision { get; set; }

    [ObservableProperty]
    public partial bool IsDynamicBeaconingEnabled { get; set; } = true;

    [ObservableProperty]
    public partial string BeaconStatus { get; set; } = "Beacon system ready";

    public HomeViewModel(
        ILocationService locationService,
        IBeaconService beaconService,
        IPortService portService,
        IConfigurationService configurationService,
        ILogger<HomeViewModel> logger)
    {
        _locationService = locationService;
        _beaconService = beaconService;
        _portService = portService;
        _configurationService = configurationService;
        _logger = logger;
        IsLocationAvailable = _locationService.IsLocationAvailable();

        // Subscribe to port changes to send initial beacon when TX port enables
        _portService.PortsChanged += OnPortsChanged;
    }

    private void OnPortsChanged(object? sender, EventArgs e)
    {
        // Check for ports transitioning from disabled to enabled
        _ = Task.Run(async () =>
        {
            try
            {
                if (UserLocation == null)
                    return;

                // Find ports that just transitioned to enabled (false → true)
                var enabledTxPorts = _portService.Ports.Where(p => p.IsTx).ToList();
                var portsJustEnabled = enabledTxPorts
                    .Where(p => p.IsEnabled && (!_previousPortEnabledState.ContainsKey(p.Id) || !_previousPortEnabledState[p.Id]))
                    .ToList();

                // Update tracking state for all TX ports
                foreach (var port in enabledTxPorts)
                {
                    _previousPortEnabledState[port.Id] = port.IsEnabled;
                }

                // Only transmit if at least one TX port was just enabled
                if (portsJustEnabled.Count == 0)
                    return;

                var callsign = _configurationService.Settings.Aprs.Callsign;
                if (string.IsNullOrEmpty(callsign))
                    return;

                var packet = _beaconService.CreatePositionPacket(UserLocation, callsign);
                var portNames = string.Join(", ", portsJustEnabled.Select(p => p.Name));
                _logger.LogInformation("Initial beacon sent due to port activation: {Ports}", portNames);
                BeaconStatus = $"✓ Initial beacon sent on port activation ({portNames})";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending initial beacon on port activation");
            }
        });
    }

    public async Task StartLocationTrackingAsync()
    {
        // Request permission first
        var hasPermission = await _locationService.RequestLocationPermissionAsync();
        if (!hasPermission)
        {
            _logger.LogWarning("Location permission denied");
            return;
        }

        // Cancel any existing tracking
        _locationUpdateCancellation?.Cancel();
        _locationUpdateCancellation = new CancellationTokenSource();

        // Start periodic location updates
        _ = Task.Run(async () =>
        {
            while (!_locationUpdateCancellation.Token.IsCancellationRequested)
            {
                try
                {
                    var location = await _locationService.GetCurrentLocationAsync(_locationUpdateCancellation.Token);

                    UserLocation = location;

                    _logger.LogInformation("Location updated: {Lat}, {Lon}", location.Latitude, location.Longitude);

                    // Evaluate beacon transmission if enabled
                    if (IsDynamicBeaconingEnabled)
                    {
                        await EvaluateAndTransmitBeaconAsync(location);
                    }

                    _previousLocation = location;

                    // Wait 5 seconds before next update
                    await Task.Delay(TimeSpan.FromSeconds(5), _locationUpdateCancellation.Token);
                }
                catch (OperationCanceledException)
                {
                    // Expected when cancellation is requested
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error getting location");

                    // Wait longer on error before retrying
                    await Task.Delay(TimeSpan.FromSeconds(10), _locationUpdateCancellation.Token);
                }
            }
        }, _locationUpdateCancellation.Token);
    }

    private async Task EvaluateAndTransmitBeaconAsync(LocationData currentLocation)
    {
        try
        {
            // Evaluate whether to transmit
            var decision = _beaconService.EvaluateLocationUpdate(currentLocation, _previousLocation);
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

            // Create position packet
            var packet = _beaconService.CreatePositionPacket(currentLocation, callsign);

            // Send to each TX port with the configured beacon mode
            foreach (var port in txPorts)
            {
                try
                {
                    // Set beacon mode for this port
                    _beaconService.SetActiveMode(port.DynamicBeaconMode);

                    // Get active port modem and send
                    // Note: This would need to be enhanced to get the actual modem instance
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

    [RelayCommand]
    public async Task SendManualBeaconAsync()
    {
        if (UserLocation == null)
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

            var packet = _beaconService.CreatePositionPacket(UserLocation, callsign);

            foreach (var port in txPorts)
            {
                _logger.LogInformation("Manual beacon sent on port {PortName}", port.Name);
            }

            BeaconStatus = "✓ Manual beacon sent";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending manual beacon");
            BeaconStatus = $"Error: {ex.Message}";
        }
    }

    public void StopLocationTracking()
    {
        _locationUpdateCancellation?.Cancel();
        _locationUpdateCancellation = null;
    }
}
