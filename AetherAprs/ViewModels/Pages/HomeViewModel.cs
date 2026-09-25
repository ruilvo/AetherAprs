// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Models;
using AetherAprs.Services;
using AetherAprs.ViewModels.Components;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace AetherAprs.ViewModels;

/// <summary>
/// Main home page ViewModel that coordinates location tracking, beacon transmission, and received beacons.
/// </summary>
public partial class HomeViewModel : ViewModelBase, IDisposable
{
    private readonly IPortService _portService;
    private readonly ILogger<HomeViewModel> _logger;
    private Dictionary<Guid, bool> _previousPortEnabledState = new();
    private LocationData? _previousLocation;
    private bool _disposed;
    private bool _hasReceivedFirstLocation;

    [ObservableProperty]
    public partial ReceivedBeaconsViewModel? ReceivedBeacons { get; set; }

    [ObservableProperty]
    public partial LocationTrackingViewModel LocationTracking { get; set; }

    [ObservableProperty]
    public partial BeaconTransmissionViewModel BeaconTransmission { get; set; }

    [ObservableProperty]
    public partial MapViewModel MapViewModel { get; set; }

    [ObservableProperty]
    public partial bool IsDynamicBeaconingEnabled { get; set; } = true;

    public HomeViewModel(
        IPortService portService,
        ReceivedBeaconsViewModel receivedBeacons,
        LocationTrackingViewModel locationTracking,
        BeaconTransmissionViewModel beaconTransmission,
        MapViewModel mapViewModel,
        ILogger<HomeViewModel> logger)
    {
        _portService = portService;
        _logger = logger;
        ReceivedBeacons = receivedBeacons;
        LocationTracking = locationTracking;
        BeaconTransmission = beaconTransmission;
        MapViewModel = mapViewModel;

        _previousPortEnabledState = _portService.Ports
            .ToDictionary(port => port.Id, port => port.IsEnabled);

        // Subscribe to events
        _portService.PortsChanged += OnPortsChanged;
        LocationTracking.LocationUpdated += OnLocationUpdated;
    }

    private async void OnLocationUpdated(object? sender, LocationData currentLocation)
    {
        try
        {
            // Update map with new location
            MapViewModel.UpdateUserLocation(currentLocation);

            // Auto-center on first location received
            if (!_hasReceivedFirstLocation)
            {
                MapViewModel.CenterOnUser();
                _hasReceivedFirstLocation = true;
            }

            // Evaluate beacon transmission if enabled
            if (IsDynamicBeaconingEnabled)
            {
                await BeaconTransmission.EvaluateAndTransmitBeaconAsync(currentLocation, _previousLocation);
            }

            _previousLocation = currentLocation;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing location update");
        }
    }

    private async void OnPortsChanged(object? sender, EventArgs e)
    {
        try
        {
            var enabledTxPorts = _portService.Ports.Where(port => port.IsTx).ToList();
            var portsJustEnabled = enabledTxPorts
                .Where(port => port.IsEnabled && !_previousPortEnabledState.GetValueOrDefault(port.Id))
                .ToList();

            _previousPortEnabledState = enabledTxPorts
                .ToDictionary(port => port.Id, port => port.IsEnabled);

            if (portsJustEnabled.Count > 0 && LocationTracking.CurrentLocation != null)
            {
                await BeaconTransmission.SendInitialBeaconOnPortActivationAsync(
                    LocationTracking.CurrentLocation,
                    portsJustEnabled);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending initial beacon on port activation");
        }
    }

    [RelayCommand]
    public async Task StartLocationTrackingAsync()
    {
        await LocationTracking.StartTrackingAsync();
    }

    [RelayCommand]
    public void StopLocationTracking()
    {
        LocationTracking.StopTracking();
    }

    [RelayCommand]
    public async Task SendManualBeaconAsync()
    {
        await BeaconTransmission.SendManualBeaconAsync(LocationTracking.CurrentLocation);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // Unsubscribe from events
        _portService.PortsChanged -= OnPortsChanged;
        if (LocationTracking != null)
        {
            LocationTracking.LocationUpdated -= OnLocationUpdated;
        }

        // Dispose sub-ViewModels
        LocationTracking?.Dispose();
        MapViewModel?.Dispose();

        // Dispose ReceivedBeacons if it implements IDisposable
        if (ReceivedBeacons is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}
