// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.IO;
using System.Linq;
using AetherAprs.Services;
using AetherAprs.ViewModels;
using AetherAprs.ViewModels.Components;
using AetherAprs.Factories;
using Avalonia.Controls;
using Avalonia.Input;
using BruTile.Cache;
using Mapsui;
using Mapsui.Tiling;
using Mapsui.UI;
using Mapsui.UI.Avalonia;

namespace AetherAprs.Views.Pages;

public partial class HomePage : UserControl
{
    // OSM requires an identifiable User-Agent; generic defaults get blocked.
    private const string OsmUserAgent =
        "AetherAprs/1.0 (+https://github.com/ruilvo/AetherAprs)";

    private HomeViewModel? _currentViewModel;

    public HomePage()
    {
        InitializeComponent();
        InitializeMap();
        DataContextChanged += OnDataContextChanged;
        MapControl.PointerPressed += OnMapPointerPressed;
    }

    private void InitializeMap()
    {
        // Initialize the map with default OpenStreetMap tiles
        MapControl.Map = new Map();
        EnsureOsmTileCache();
        MapControl.Map.Layers.Add(OpenStreetMap.CreateTileLayer(OsmUserAgent), group: -1);
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        // Unsubscribe from old ViewModel to prevent memory leak
        if (_currentViewModel?.MapViewModel != null)
        {
            _currentViewModel.MapViewModel.CenterOnPointRequested -= OnCenterOnPointRequested;
        }

        if (DataContext is not HomeViewModel viewModel)
        {
            _currentViewModel = null;
            return;
        }

        _currentViewModel = viewModel;

        // Add map layers from ViewModels
        if (viewModel.MapViewModel?.UserLocationLayer != null)
        {
            MapControl.Map?.Layers.Add(viewModel.MapViewModel.UserLocationLayer, group: 1);
        }

        if (viewModel.ReceivedBeacons != null && MapControl.Map != null)
        {
            MapControl.Map.Layers.Add(viewModel.ReceivedBeacons.TrailsLayer, group: 0);
            MapControl.Map.Layers.Add(viewModel.ReceivedBeacons.BeaconsLayer, group: 1);
        }

        // Subscribe to map centering requests
        if (viewModel.MapViewModel != null)
        {
            viewModel.MapViewModel.CenterOnPointRequested += OnCenterOnPointRequested;
        }

        // Start location tracking at runtime, not in designer
        if (!Design.IsDesignMode)
        {
            _ = viewModel.StartLocationTrackingAsync();
        }
    }

    private void OnCenterOnPointRequested(object? sender, MPoint mapPoint)
    {
        var navigator = MapControl.Map?.Navigator;
        if (navigator is null)
        {
            return;
        }

        // Prefer CenterOnAndZoomTo when resolutions are available
        if (navigator.Resolutions.Count > 9)
        {
            navigator.CenterOnAndZoomTo(mapPoint, navigator.Resolutions[9]);
        }
        else
        {
            navigator.CenterOn(mapPoint);
            navigator.ZoomTo(2000);
        }
    }

    private void OnMapPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_currentViewModel == null || MapControl.Map == null)
        {
            return;
        }

        var screenPosition = e.GetPosition(MapControl);
        var viewport = MapControl.Map.Navigator.Viewport;
        
        // Convert screen to world coordinates manually
        var worldX = viewport.CenterX + (screenPosition.X - viewport.Width / 2.0) * viewport.Resolution;
        var worldY = viewport.CenterY - (screenPosition.Y - viewport.Height / 2.0) * viewport.Resolution;
        
        // Query features at clicked position (with tolerance)
        var tolerance = viewport.Resolution * 20; // 20 pixel tolerance
        var clickedFeatures = MapControl.Map.Layers
            .SelectMany(layer => layer.GetFeatures(new MRect(
                worldX - tolerance,
                worldY - tolerance,
                worldX + tolerance,
                worldY + tolerance), viewport.Resolution))
            .ToList();

        // Find the first beacon feature with a callsign
        foreach (var feature in clickedFeatures)
        {
            if (feature.Fields != null && feature.Fields.Contains("Callsign"))
            {
                var callsign = feature["Callsign"] as string;
                if (!string.IsNullOrEmpty(callsign))
                {
                    // Navigate to packet details
                    var factory = App.GetService<IPacketDetailsViewModelFactory>();
                    var navigationService = App.GetService<INavigationService>();
                    var vm = factory.Create(callsign);
                    navigationService.NavigateTo(vm);
                    e.Handled = true;
                    return;
                }
            }
        }
    }

    private static void EnsureOsmTileCache()
    {
        if (Design.IsDesignMode || OpenStreetMap.DefaultCache is not null)
        {
            return;
        }

        var appDataDirService = App.GetService<IAppDataDirProviderService>();
        if (appDataDirService == null)
        {
            return;
        }

        var appDataDir = appDataDirService.GetAppDataDirectory();
        var cacheDir = Path.Combine(appDataDir, "osm-tile-cache");
        OpenStreetMap.DefaultCache = new FileCache(cacheDir, "png");
    }
}
