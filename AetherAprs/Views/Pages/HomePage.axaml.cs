// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Linq;
using System.Threading.Tasks;
using AetherAprs.ViewModels;
using AetherAprs.ViewModels.Components;
using AetherAprs.ViewModels.Pages;
using Avalonia.Controls;
using Avalonia.Input;
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
        // OSM tile cache is initialized during app startup in App.axaml.cs
        MapControl.Map.Layers.Add(OpenStreetMap.CreateTileLayer(OsmUserAgent), group: -1);
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        // Unsubscribe from old ViewModel to prevent memory leak
        if (_currentViewModel?.Map != null)
        {
            _currentViewModel.Map.CenterOnPointRequested -= OnCenterOnPointRequested;
        }

        if (DataContext is not HomeViewModel viewModel)
        {
            _currentViewModel = null;
            return;
        }

        _currentViewModel = viewModel;

        // Register the map layers owned by services injected into the ViewModel
        if (MapControl.Map != null)
        {
            if (!MapControl.Map.Layers.Contains(viewModel.UserLocationLayer))
            {
                MapControl.Map.Layers.Add(viewModel.UserLocationLayer, group: 1);
            }

            if (!MapControl.Map.Layers.Contains(viewModel.BeaconTrailsLayer))
            {
                MapControl.Map.Layers.Add(viewModel.BeaconTrailsLayer, group: 0);
            }

            if (!MapControl.Map.Layers.Contains(viewModel.BeaconsLayer))
            {
                MapControl.Map.Layers.Add(viewModel.BeaconsLayer, group: 1);
            }
        }

        // Subscribe to map centering requests
        if (viewModel.Map != null)
        {
            viewModel.Map.CenterOnPointRequested += OnCenterOnPointRequested;

            // The first location fix can arrive before this view is created (tracking
            // starts at app startup and the fused provider returns almost instantly),
            // in which case the auto-center event fires with no subscriber attached.
            // Re-issue the request; this is a no-op while no location is known yet.
            viewModel.Map.CenterOnUser();
        }

        // Start location tracking at runtime, not in designer
        if (!Design.IsDesignMode)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await viewModel.StartLocationTrackingAsync();
                }
                catch (Exception ex)
                {
                    // Log error - location tracking failures shouldn't crash the UI
                    System.Diagnostics.Debug.WriteLine($"Error starting location tracking: {ex}");
                }
            });
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
                    // Delegate to ViewModel for navigation
                    _currentViewModel.OnBeaconClicked(callsign);
                    e.Handled = true;
                    return;
                }
            }
        }
    }
}
