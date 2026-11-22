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
using Mapsui.Layers;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AetherAprs.ViewModels.Pages;

/// <summary>
/// Main home page ViewModel that coordinates location tracking, beacon transmission, and map display.
/// </summary>
public partial class HomeViewModel : ViewModelBase, IDisposable
{
    private readonly ILocationTrackingService _locationTrackingService;
    private readonly IBeaconTransmissionService _beaconTransmissionService;
    private readonly IUserLocationLayerService _userLocationLayerService;
    private readonly IReceivedBeaconsMapLayerService _receivedBeaconsLayerService;
    private readonly IMapTileSourceService _mapTileSourceService;
    private readonly IPortService _portService;
    private readonly IPacketDetailsViewModelFactory _packetDetailsFactory;
    private readonly IObjectDetailsViewModelFactory _objectDetailsFactory;
    private readonly INavigationService _navigationService;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly ILogger<HomeViewModel> _logger;
    private readonly HashSet<Guid> _initialBeaconSentPorts = [];
    private readonly SemaphoreSlim _initialBeaconLock = new(1, 1);
    private bool _disposed;
    private bool _hasReceivedFirstLocation;

    [ObservableProperty]
    public partial MapViewModel Map { get; set; }

    [ObservableProperty]
    public partial bool IsDynamicBeaconingEnabled { get; set; } = true;

    [ObservableProperty]
    public partial string BeaconStatus { get; set; } = string.Empty;

    /// <summary>
    /// Map layers owned by the injected layer services. The Home view reads
    /// these to register them on the MapControl without touching the DI container.
    /// </summary>
    public ILayer UserLocationLayer => _userLocationLayerService.Layer;
    public ILayer BeaconTrailsLayer => _receivedBeaconsLayerService.TrailsLayer;
    public ILayer BeaconsLayer => _receivedBeaconsLayerService.BeaconsLayer;
    public ILayer BaseMapLayer { get; }

    public HomeViewModel(
        ILocationTrackingService locationTrackingService,
        IBeaconTransmissionService beaconTransmissionService,
        IUserLocationLayerService userLocationLayerService,
        IReceivedBeaconsMapLayerService receivedBeaconsLayerService,
        IMapTileSourceService mapTileSourceService,
        IPortService portService,
        MapViewModel mapViewModel,
        IPacketDetailsViewModelFactory packetDetailsFactory,
        IObjectDetailsViewModelFactory objectDetailsFactory,
        INavigationService navigationService,
        IUiDispatcher uiDispatcher,
        ILogger<HomeViewModel> logger)
    {
        _locationTrackingService = locationTrackingService;
        _beaconTransmissionService = beaconTransmissionService;
        _userLocationLayerService = userLocationLayerService;
        _receivedBeaconsLayerService = receivedBeaconsLayerService;
        _mapTileSourceService = mapTileSourceService;
        _portService = portService;
        _packetDetailsFactory = packetDetailsFactory;
        _objectDetailsFactory = objectDetailsFactory;
        _navigationService = navigationService;
        _uiDispatcher = uiDispatcher;
        _logger = logger;
        Map = mapViewModel;
        BaseMapLayer = _mapTileSourceService.CreateBaseMapLayer();

        // Subscribe to events
        _portService.PortsChanged += OnPortsChanged;
        _locationTrackingService.LocationUpdated += OnLocationUpdated;
        _beaconTransmissionService.BeaconStatusChanged += OnBeaconStatusChanged;

        // Initialize beacon status
        BeaconStatus = _beaconTransmissionService.BeaconStatus;
    }

    private void OnBeaconStatusChanged(object? sender, string status)
    {
        // Events arrive on background threads; observable properties must update on the UI thread.
        _uiDispatcher.Post(() => BeaconStatus = status);
    }

    private void OnLocationUpdated(object? sender, LocationData currentLocation)
    {
        // LocationTrackingService polls on a threadpool thread; marshal all map/UI
        // mutation onto the UI thread.
        _uiDispatcher.Post(() =>
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
                // If follow mode is enabled, continuously center and rotate map
                else if (Map.IsFollowModeEnabled)
                {
                    Map.CenterOnUser();
                }

                // Evaluate beacon transmission if enabled
                if (IsDynamicBeaconingEnabled)
                {
                    _beaconTransmissionService.ProcessLocationUpdate(currentLocation);
                }

                _ = ReconcileInitialBeaconsAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing location update");
            }
        });
    }

    private async void OnPortsChanged(object? sender, EventArgs e)
    {
        try
        {
            await ReconcileInitialBeaconsAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending initial beacon on port activation");
        }
    }

    private async Task ReconcileInitialBeaconsAsync()
    {
        var location = _locationTrackingService.CurrentLocation;
        if (location is null)
        {
            return;
        }

        await _initialBeaconLock.WaitAsync().ConfigureAwait(false);
        try
        {
            var ports = _portService.Ports;
            _initialBeaconSentPorts.RemoveWhere(portId =>
                !ports.Any(port => port.Id == portId && port.IsEnabled && port.IsTx));

            var portsToBeacon = ports
                .Where(port => port.IsEnabled && port.IsTx && !_initialBeaconSentPorts.Contains(port.Id))
                .ToList();

            if (portsToBeacon.Count == 0)
            {
                return;
            }

            await _beaconTransmissionService.SendInitialBeaconOnPortActivationAsync(
                location,
                portsToBeacon).ConfigureAwait(false);

            foreach (var port in portsToBeacon)
            {
                _initialBeaconSentPorts.Add(port.Id);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reconciling initial beacons after startup state change");
        }
        finally
        {
            _initialBeaconLock.Release();
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
    /// Forwards viewport resolution changes to the beacons layer service so it can
    /// re-cluster markers as the user zooms.
    /// </summary>
    public void UpdateMapResolution(double resolution)
    {
        _receivedBeaconsLayerService.SetResolution(resolution);
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

    /// <summary>
    /// Handles object/item click events from the map view.
    /// </summary>
    /// <param name="name">The name of the clicked object or item.</param>
    public void OnObjectClicked(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return;
        }

        var vm = _objectDetailsFactory.Create(name);
        _navigationService.NavigateTo(vm);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _portService.PortsChanged -= OnPortsChanged;
        _locationTrackingService.LocationUpdated -= OnLocationUpdated;
        _beaconTransmissionService.BeaconStatusChanged -= OnBeaconStatusChanged;
        _initialBeaconLock.Dispose();
    }
}
