// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Linq;

using AetherAprs.Services.UI;
using AetherAprs.ViewModels.Pages;

using Avalonia.Controls;

using Mapsui;
using Mapsui.Animations;
using Mapsui.Manipulations;
using Mapsui.UI.Avalonia;

namespace AetherAprs.Views.Pages;

public partial class HomePage : UserControl
{
    private HomeViewModel? _currentViewModel;
    private MPoint? _pendingCenterPoint;
    private bool _mapEventsWired;

    public HomePage()
    {
        InitializeComponent();
        InitializeMap();
        DataContextChanged += OnDataContextChanged;
    }

    private void InitializeMap()
    {
        MapControl.Map = new Map();
    }

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        WireMapEvents();
        BindViewModel(DataContext as HomeViewModel);
        TryApplyPendingCenter();
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        // The page is discarded when the user navigates away, but the layers and the
        // map projection belong to application-lifetime services. Drop every handler
        // and layer reference this page took, then leave the services untouched.
        UnbindViewModel();
        UnwireMapEvents();
        _pendingCenterPoint = null;
        base.OnDetachedFromVisualTree(e);
    }

    private void WireMapEvents()
    {
        if (_mapEventsWired)
        {
            return;
        }

        _mapEventsWired = true;
        MapControl.MapTapped += OnMapTapped;
        MapControl.PointerPressed += OnMapPointerPressed;
        Loaded += OnLayoutChanged;
        SizeChanged += OnLayoutChanged;

        if (MapControl.Map is { } map)
        {
            map.Navigator.ViewportChanged += OnViewportChanged;
        }
    }

    private void UnwireMapEvents()
    {
        if (!_mapEventsWired)
        {
            return;
        }

        _mapEventsWired = false;
        MapControl.MapTapped -= OnMapTapped;
        MapControl.PointerPressed -= OnMapPointerPressed;
        Loaded -= OnLayoutChanged;
        SizeChanged -= OnLayoutChanged;

        if (MapControl.Map is { } map)
        {
            map.Navigator.ViewportChanged -= OnViewportChanged;
            map.Layers.Clear();
        }
    }

    private void OnLayoutChanged(object? sender, EventArgs e) => TryApplyPendingCenter();

    private void BindViewModel(HomeViewModel? viewModel)
    {
        if (ReferenceEquals(_currentViewModel, viewModel))
        {
            return;
        }

        UnbindViewModel();
        _currentViewModel = viewModel;

        if (viewModel is null || MapControl.Map is not { } map)
        {
            return;
        }

        // Register the map layers owned by services injected into the ViewModel.
        if (!map.Layers.Contains(viewModel.BaseMapLayer))
        {
            map.Layers.Add(viewModel.BaseMapLayer, group: -1);
        }

        if (!map.Layers.Contains(viewModel.UserLocationLayer))
        {
            map.Layers.Add(viewModel.UserLocationLayer, group: 1);
        }

        if (!map.Layers.Contains(viewModel.BeaconTrailsLayer))
        {
            map.Layers.Add(viewModel.BeaconTrailsLayer, group: 0);
        }

        if (!map.Layers.Contains(viewModel.BeaconsLayer))
        {
            map.Layers.Add(viewModel.BeaconsLayer, group: 1);
        }

        // Subscribe to map centering/zoom/rotation requests
        if (viewModel.Map is { } mapModel)
        {
            mapModel.CenterOnPointRequested += OnCenterOnPointRequested;
            mapModel.ZoomInRequested += OnZoomInRequested;
            mapModel.ZoomOutRequested += OnZoomOutRequested;

            // The first location fix can arrive before this view is created (tracking
            // starts at app startup and the fused provider returns almost instantly),
            // in which case the auto-center event fires with no subscriber attached.
            // Re-issue the request; this is a no-op while no location is known yet.
            mapModel.CenterOnUser();
        }
    }

    private void UnbindViewModel()
    {
        if (_currentViewModel is null)
        {
            return;
        }

        if (MapControl.Map is { } map)
        {
            map.Layers.Remove(_currentViewModel.BaseMapLayer);
            map.Layers.Remove(_currentViewModel.UserLocationLayer);
            map.Layers.Remove(_currentViewModel.BeaconTrailsLayer);
            map.Layers.Remove(_currentViewModel.BeaconsLayer);
        }

        if (_currentViewModel.Map is { } mapModel)
        {
            mapModel.CenterOnPointRequested -= OnCenterOnPointRequested;
            mapModel.ZoomInRequested -= OnZoomInRequested;
            mapModel.ZoomOutRequested -= OnZoomOutRequested;
        }

        _currentViewModel = null;
    }

    private void OnViewportChanged(object? sender, ViewportChangedEventArgs e) =>
        // Forward resolution changes so the beacons layer can re-cluster as the user zooms.
        _currentViewModel?.UpdateMapResolution(e.Viewport.Resolution);

    private void OnMapPointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e) =>
        // User is interacting with the map - disable follow mode
        _currentViewModel?.Map?.OnUserInteraction();

    private void OnDataContextChanged(object? sender, EventArgs e) =>
        BindViewModel(DataContext as HomeViewModel);

    private void OnCenterOnPointRequested(object? sender, MPoint mapPoint)
    {
        Navigator? navigator = MapControl.Map?.Navigator;
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

        Navigator? navigator = MapControl.Map?.Navigator;
        if (navigator is null || !HasMapViewport(navigator))
        {
            return;
        }

        _pendingCenterPoint = null;
        ApplyCenterPoint(navigator, mapPoint);
    }

    private static bool HasMapViewport(Navigator navigator)
    {
        Viewport viewport = navigator.Viewport;
        return viewport.Width > 0 && viewport.Height > 0;
    }

    private static void ApplyCenterPoint(Navigator navigator, MPoint mapPoint)
    {
        // Center, zoom and reset north in a single animated viewport. Mapsui keeps
        // one animation per navigator, so firing CenterOnAndZoomTo and RotateTo as
        // two separate calls made the rotation animation replace the centering
        // one, which left the map north-up but never centered or zoomed. One
        // SetViewport animates every field (center, resolution, rotation) together.
        Viewport target = MapCentering.CreateCenteredViewport(
            navigator.Viewport,
            mapPoint,
            navigator.Resolutions);
        navigator.SetViewport(target, 350, Easing.CubicOut);
    }

    private void OnZoomInRequested(object? sender, EventArgs e) => MapControl.Map?.Navigator.ZoomIn(300, Easing.CubicOut);

    private void OnZoomOutRequested(object? sender, EventArgs e) => MapControl.Map?.Navigator.ZoomOut(300, Easing.CubicOut);

    private void OnMapTapped(object? sender, MapEventArgs e)
    {
        // MapTapped only fires for a genuine tap (single/double/long-press), never
        // for a pinch or drag, so a station between two pinching fingers no longer
        // triggers navigation. We only act on single taps to avoid double-tap zoom
        // and long-press gestures opening details.
        if (e.GestureType != GestureType.SingleTap)
        {
            return;
        }

        if (_currentViewModel == null || MapControl.Map == null)
        {
            return;
        }

        Viewport viewport = MapControl.Map.Navigator.Viewport;
        MapTapResult result = MapTapResolver.Resolve(
            MapControl.Map.Layers,
            e.WorldPosition,
            viewport.Resolution);

        switch (result)
        {
            case MapTapResult.ZoomToCluster cluster:
                MapControl.Map.Navigator.CenterOnAndZoomTo(
                    cluster.Center,
                    cluster.Resolution,
                    350,
                    Easing.CubicOut);
                e.Handled = true;
                break;

            case MapTapResult.OpenObject openObject:
                _currentViewModel.OnObjectClicked(openObject.Name);
                e.Handled = true;
                break;

            case MapTapResult.OpenBeacon openBeacon:
                _currentViewModel.OnBeaconClicked(openBeacon.Callsign);
                e.Handled = true;
                break;
        }
    }
}
