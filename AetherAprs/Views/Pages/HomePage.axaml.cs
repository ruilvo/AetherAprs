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
using Mapsui.Animations;
using Mapsui.Layers;
using Mapsui.UI;
using Mapsui.UI.Avalonia;

namespace AetherAprs.Views.Pages;

public partial class HomePage : UserControl
{
    private HomeViewModel? _currentViewModel;
    private MPoint? _pendingCenterPoint;

    public HomePage()
    {
        InitializeComponent();
        InitializeMap();
        DataContextChanged += OnDataContextChanged;
        MapControl.PointerPressed += OnMapPointerPressed;
        Loaded += (_, _) => TryApplyPendingCenter();
        SizeChanged += (_, _) => TryApplyPendingCenter();
    }

    private void InitializeMap()
    {
        MapControl.Map = new Map();
        MapControl.Map.Navigator.ViewportChanged += OnViewportChanged;
    }

    private void OnViewportChanged(object? sender, ViewportChangedEventArgs e)
    {
        // Forward resolution changes so the beacons layer can re-cluster as the user zooms.
        _currentViewModel?.UpdateMapResolution(e.Viewport.Resolution);
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        // Unsubscribe from old ViewModel to prevent memory leak
        if (_currentViewModel?.Map != null)
        {
            _currentViewModel.Map.CenterOnPointRequested -= OnCenterOnPointRequested;
            _currentViewModel.Map.ZoomInRequested -= OnZoomInRequested;
            _currentViewModel.Map.ZoomOutRequested -= OnZoomOutRequested;
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
            if (!MapControl.Map.Layers.Contains(viewModel.BaseMapLayer))
            {
                MapControl.Map.Layers.Add(viewModel.BaseMapLayer, group: -1);
            }

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

        // Subscribe to map centering/zoom requests
        if (viewModel.Map != null)
        {
            viewModel.Map.CenterOnPointRequested += OnCenterOnPointRequested;
            viewModel.Map.ZoomInRequested += OnZoomInRequested;
            viewModel.Map.ZoomOutRequested += OnZoomOutRequested;

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
        if (navigator is null || !HasMapViewport(navigator))
        {
            _pendingCenterPoint = mapPoint;
            return;
        }

        ApplyCenterPoint(navigator, mapPoint);
    }

    private void TryApplyPendingCenter()
    {
        if (_pendingCenterPoint is not { } mapPoint)
        {
            return;
        }

        var navigator = MapControl.Map?.Navigator;
        if (navigator is null || !HasMapViewport(navigator))
        {
            return;
        }

        _pendingCenterPoint = null;
        ApplyCenterPoint(navigator, mapPoint);
    }

    private static bool HasMapViewport(Navigator navigator)
    {
        var viewport = navigator.Viewport;
        return viewport.Width > 0 && viewport.Height > 0;
    }

    private static void ApplyCenterPoint(Navigator navigator, MPoint mapPoint)
    {
        // Prefer an animated CenterOnAndZoomTo when resolutions are available.
        if (navigator.Resolutions.Count > 9)
        {
            navigator.CenterOnAndZoomTo(mapPoint, navigator.Resolutions[9], 350, Easing.CubicOut);
        }
        else
        {
            navigator.CenterOn(mapPoint, 350, Easing.CubicOut);
            navigator.ZoomTo(2000, 350, Easing.CubicOut);
        }
    }

    private void OnZoomInRequested(object? sender, EventArgs e)
    {
        MapControl.Map?.Navigator.ZoomIn(300, Easing.CubicOut);
    }

    private void OnZoomOutRequested(object? sender, EventArgs e)
    {
        MapControl.Map?.Navigator.ZoomOut(300, Easing.CubicOut);
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

        // Find the first cluster or beacon feature at the click point.
        foreach (var feature in clickedFeatures)
        {
            if (feature.Fields == null)
            {
                continue;
            }

            // Cluster click: zoom in (animated), centered on the cluster centroid.
            if (feature.Fields.Contains("Cluster") && feature is PointFeature clusterFeature)
            {
                MapControl.Map.Navigator.CenterOnAndZoomTo(
                    clusterFeature.Point,
                    viewport.Resolution * 0.5,
                    350,
                    Easing.CubicOut);
                e.Handled = true;
                return;
            }

            // Beacon click: open station details.
            if (feature.Fields.Contains("Callsign"))
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
