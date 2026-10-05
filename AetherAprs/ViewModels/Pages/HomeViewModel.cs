// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Factories.ViewModels;
using AetherAprs.Models;
using AetherAprs.Services.Location;
using AetherAprs.Services.Ports;
using AetherAprs.Services.Transmission;
using AetherAprs.Services.UI;
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
/// Main home page ViewModel that coordinates location tracking, beacon transmission, and map display.
/// </summary>
public partial class HomeViewModel : ViewModelBase, IDisposable
{
    private readonly ILocationTrackingService _locationTrackingService;
    private readonly IBeaconTransmissionService _beaconTransmissionService;
    private readonly IUserLocationLayerService _userLocationLayerService;
    private readonly IPortService _portService;
    private readonly IPacketDetailsViewModelFactory _packetDetailsFactory;
    private readonly INavigationService _navigationService;
    private readonly ILogger<HomeViewModel> _logger;
    private Dictionary<Guid, bool> _previousPortEnabledState = new();
    private bool _disposed;
    private bool _hasReceivedFirstLocation;

    [ObservableProperty]
    public partial MapViewModel Map { get; set; }

    [ObservableProperty]
    public partial bool IsDynamicBeaconingEnabled { get; set; } = true;

    [ObservableProperty]
    public partial string BeaconStatus { get; set; } = string.Empty;

    public HomeViewModel(
        ILocationTrackingService locationTrackingService,
        IBeaconTransmissionService beaconTransmissionService,
        IUserLocationLayerService userLocationLayerService,
        IPortService portService,
        MapViewModel mapViewModel,
        IPacketDetailsViewModelFactory packetDetailsFactory,
        INavigationService navigationService,
        ILogger<HomeViewModel> logger)
    {
        _locationTrackingService = locationTrackingService;
        _beaconTransmissionService = beaconTransmissionService;
        _userLocationLayerService = userLocationLayerService;
        _portService = portService;
        _packetDetailsFactory = packetDetailsFactory;
        _navigationService = navigationService;
        _logger = logger;
        Map = mapViewModel;

        _previousPortEnabledState = _portService.Ports
            .ToDictionary(port => port.Id, port => port.IsEnabled);

        // Subscribe to events
        _portService.PortsChanged += OnPortsChanged;
        _locationTrackingService.LocationUpdated += OnLocationUpdated;
        _beaconTransmissionService.BeaconStatusChanged += OnBeaconStatusChanged;

        // Initialize beacon status
        BeaconStatus = _beaconTransmissionService.BeaconStatus;
    }

    private void OnBeaconStatusChanged(object? sender, string status)
    {
        BeaconStatus = status;
    }

    private async void OnLocationUpdated(object? sender, LocationData currentLocation)
    {
        try
        {
            // Update map with new location
            _userLocationLayerService.UpdateLocation(currentLocation);

            // Auto-center on first location received
            if (!_hasReceivedFirstLocation)
            {
                Map.CenterOnUser();
                _hasReceivedFirstLocation = true;
            }

            // Evaluate beacon transmission if enabled
            if (IsDynamicBeaconingEnabled)
            {
                _beaconTransmissionService.ProcessLocationUpdate(currentLocation);
            }
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

            if (portsJustEnabled.Count > 0 && _locationTrackingService.CurrentLocation != null)
            {
                await _beaconTransmissionService.SendInitialBeaconOnPortActivationAsync(
                    _locationTrackingService.CurrentLocation,
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
        await _locationTrackingService.StartTrackingAsync();
    }

    [RelayCommand]
    public void StopLocationTracking()
    {
        _locationTrackingService.StopTracking();
    }

    [RelayCommand]
    public async Task SendManualBeaconAsync()
    {
        await _beaconTransmissionService.SendManualBeaconAsync(_locationTrackingService.CurrentLocation);
    }

    /// <summary>
    /// Handles beacon click events from the map view.
    /// </summary>
    /// <param name="callsign">The callsign of the clicked beacon.</param>
    public void OnBeaconClicked(string callsign)
    {
        if (string.IsNullOrEmpty(callsign))
        {
            return;
        }

        var vm = _packetDetailsFactory.Create(callsign);
        _navigationService.NavigateTo(vm);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _portService.PortsChanged -= OnPortsChanged;
        _locationTrackingService.LocationUpdated -= OnLocationUpdated;
        _beaconTransmissionService.BeaconStatusChanged -= OnBeaconStatusChanged;
    }
}
