// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Configuration;
using AetherAprs.Factories.ViewModels;
using AetherAprs.Localization;
using AetherAprs.Models;
using AetherAprs.Services.Location;
using AetherAprs.Services.Platform;
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
    private readonly IPermissionAttentionReporter _permissionAttention;
    private readonly IPermissionService _permissionService;
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
    /// Gets or sets a value indicating whether a permission currently keeps part
    /// of the application from working.
    /// </summary>
    [ObservableProperty]
    public partial bool HasPermissionWarning { get; set; }

    /// <summary>
    /// Gets or sets the localized explanation of what is blocked and what the
    /// operator can do about it.
    /// </summary>
    [ObservableProperty]
    public partial string PermissionWarningText { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether only the application settings can
    /// grant the blocked permission again.
    /// </summary>
    [ObservableProperty]
    public partial bool CanOpenPermissionSettings { get; set; }

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
        IPermissionAttentionReporter permissionAttention,
        IPermissionService permissionService,
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
        _permissionAttention = permissionAttention;
        _permissionService = permissionService;
        _logger = logger;
        Map = mapViewModel;
        BaseMapLayer = _mapTileSourceService.CreateBaseMapLayer();

        // Subscribe to events
        _portService.PortsChanged += OnPortsChanged;
        _locationTrackingService.LocationUpdated += OnLocationUpdated;
        _beaconTransmissionService.BeaconStatusChanged += OnBeaconStatusChanged;
        _permissionAttention.Changed += OnPermissionAttentionChanged;

        // Initialize beacon status
        BeaconStatus = _beaconTransmissionService.BeaconStatus;
        RefreshPermissionWarning();
    }

    private void OnBeaconStatusChanged(object? sender, string status) =>
        // Events arrive on background threads; observable properties must update on the UI thread.
        _uiDispatcher.Post(() => BeaconStatus = status);

    private void OnPermissionAttentionChanged(object? sender, EventArgs e) =>
        _uiDispatcher.Post(RefreshPermissionWarning);

    /// <summary>
    /// Rebuilds the permission banner from the outcomes that still need the
    /// operator. A granted or unavailable permission leaves nothing to act on, so
    /// only denials reach the banner.
    /// </summary>
    private void RefreshPermissionWarning()
    {
        IReadOnlyList<PermissionAttention> outstanding = _permissionAttention.Outstanding;
        if (outstanding.Count == 0)
        {
            HasPermissionWarning = false;
            CanOpenPermissionSettings = false;
            PermissionWarningText = string.Empty;
            return;
        }

        string names = string.Join(", ", outstanding.Select(attention => PermissionName(attention.Kind)));
        bool requiresSettings = outstanding.Any(attention => attention.RequiresApplicationSettings);
        bool limitsBackgroundTracking = outstanding.Any(attention =>
            attention.Kind == PermissionKind.BackgroundLocation);

        HasPermissionWarning = true;

        // Background location can only be changed in the application settings, and a
        // refusal limits tracking rather than breaking it, so it gets its own wording
        // instead of the generic denial sentence.
        CanOpenPermissionSettings = requiresSettings || limitsBackgroundTracking;
        PermissionWarningText = Strings.Format(
            limitsBackgroundTracking
                ? "PermissionWarningBackgroundLocation"
                : requiresSettings
                    ? "PermissionWarningPermanentlyDenied"
                    : "PermissionWarningDenied",
            names);
    }

    private static string PermissionName(PermissionKind kind) => kind switch
    {
        PermissionKind.Notifications => Strings.Get("PermissionKindNotifications"),
        PermissionKind.Bluetooth => Strings.Get("PermissionKindBluetooth"),
        PermissionKind.Location => Strings.Get("Location"),
        PermissionKind.BackgroundLocation => Strings.Get("PermissionKindBackgroundLocation"),
        PermissionKind.Audio => Strings.Get("PermissionKindAudio"),
        PermissionKind.BatteryOptimization => Strings.Get("PermissionKindBatteryOptimization"),
        _ => kind.ToString()
    };

    [RelayCommand]
    private async Task OpenPermissionSettingsAsync()
    {
        if (!CanOpenPermissionSettings)
        {
            return;
        }

        try
        {
            if (!await _permissionService.OpenApplicationSettingsAsync().ConfigureAwait(false))
            {
                _logger.LogWarning("The platform did not open the application settings");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Opening the application settings failed");
        }
    }

    private void OnLocationUpdated(object? sender, LocationData currentLocation) =>
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
        LocationData? location = _locationTrackingService.CurrentLocation;
        if (location is null)
        {
            return;
        }

        await _initialBeaconLock.WaitAsync().ConfigureAwait(false);
        try
        {
            IReadOnlyList<PortConfig> ports = _portService.Ports;
            _initialBeaconSentPorts.RemoveWhere(portId =>
                !ports.Any(port => port.Id == portId && port.IsEnabled && port.IsTx));

            List<PortConfig> portsToBeacon = [.. ports.Where(port => port.IsEnabled && port.IsTx && !_initialBeaconSentPorts.Contains(port.Id))];

            if (portsToBeacon.Count == 0)
            {
                return;
            }

            await _beaconTransmissionService.SendInitialBeaconOnPortActivationAsync(
                location,
                portsToBeacon).ConfigureAwait(false);

            foreach (PortConfig port in portsToBeacon)
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
    public async Task StartLocationTrackingAsync() =>
        // Permission belongs to the feature: the prompt appears when the operator
        // starts tracking, not on every launch.
        await _locationTrackingService.RequestPermissionAndStartTrackingAsync();

    [RelayCommand]
    public void StopLocationTracking() => _locationTrackingService.StopTracking();

    [RelayCommand]
    public async Task SendManualBeaconAsync() => await _beaconTransmissionService.SendManualBeaconAsync(_locationTrackingService.CurrentLocation);

    /// <summary>
    /// Forwards viewport resolution changes to the beacons layer service so it can
    /// re-cluster markers as the user zooms.
    /// </summary>
    public void UpdateMapResolution(double resolution) => _receivedBeaconsLayerService.SetResolution(resolution);

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

        PacketDetailsViewModel vm = _packetDetailsFactory.Create(callsign);
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

        ObjectDetailsViewModel vm = _objectDetailsFactory.Create(name);
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
        _permissionAttention.Changed -= OnPermissionAttentionChanged;
        _initialBeaconLock.Dispose();
    }
}
