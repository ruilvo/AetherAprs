// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Models;
using AetherAprs.Configuration;
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
        _previousPortEnabledState = _portService.Ports
            .ToDictionary(port => port.Id, port => port.IsEnabled);

        _portService.PortsChanged += OnPortsChanged;
    }

    private async void OnPortsChanged(object? sender, EventArgs e)
    {
        try
        {
            var enabledTxPorts = _portService.Ports.Where(port => port.IsTx).ToList();
            var portsJustEnabled = enabledTxPorts
                .Where(port => port.IsEnabled && (!_previousPortEnabledState.TryGetValue(port.Id, out var wasEnabled) || !wasEnabled))
                .ToList();

            _previousPortEnabledState = enabledTxPorts
                .ToDictionary(port => port.Id, port => port.IsEnabled);

            if (UserLocation == null || portsJustEnabled.Count == 0)
                return;

            var callsign = _configurationService.Settings.Aprs.Callsign;
            if (string.IsNullOrEmpty(callsign))
                return;

            var sentPortNames = new List<string>();
            foreach (var port in portsJustEnabled)
            {
                try
                {
                    _beaconService.SetActiveMode(GetPortBeaconMode(port));
                    var packet = _beaconService.CreatePositionPacket(
                        UserLocation,
                        GetPortCallsign(port, callsign),
                        GetPortSymbolTableCharacter(port),
                        GetPortSymbolCodeCharacter(port));
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending initial beacon on port activation");
        }
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

            // Send to each TX port with the configured beacon mode
            var sentPortCount = 0;
            foreach (var port in txPorts)
            {
                try
                {
                    _beaconService.SetActiveMode(GetPortBeaconMode(port));
                    var packet = _beaconService.CreatePositionPacket(
                        currentLocation,
                        GetPortCallsign(port, callsign),
                        GetPortSymbolTableCharacter(port),
                        GetPortSymbolCodeCharacter(port));
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

            var sentPortCount = 0;
            foreach (var port in txPorts)
            {
                try
                {
                    _beaconService.SetActiveMode(GetPortBeaconMode(port));
                    var packet = _beaconService.CreatePositionPacket(
                        UserLocation,
                        GetPortCallsign(port, callsign),
                        GetPortSymbolTableCharacter(port),
                        GetPortSymbolCodeCharacter(port));
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

    public void StopLocationTracking()
    {
        _locationUpdateCancellation?.Cancel();
        _locationUpdateCancellation = null;
    }

    private int? GetPortSsid(PortConfig port)
    {
        var ssid = port.Ssid ?? _configurationService.Settings.Aprs.DefaultSsid;
        return ssid > 0 ? ssid : null;
    }

    private string GetPortCallsign(PortConfig port, string callsign)
    {
        var ssid = GetPortSsid(port);
        return ssid.HasValue ? $"{callsign}-{ssid.Value}" : callsign;
    }

    private DynamicBeaconMode GetPortBeaconMode(PortConfig port) =>
        port.DynamicBeaconMode ?? _configurationService.Settings.Aprs.DefaultBeaconMode;

    private string GetPortSymbolTableCharacter(PortConfig port) =>
        port.SymbolTableCharacter ?? _configurationService.Settings.Aprs.DefaultSymbolTableCharacter;

    private string GetPortSymbolCodeCharacter(PortConfig port) =>
        port.SymbolCodeCharacter ?? _configurationService.Settings.Aprs.DefaultSymbolCodeCharacter;
}
