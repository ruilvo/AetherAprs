// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later
using AetherAprs.Models;
using AetherAprs.ViewModels;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Projections;
using Mapsui.Styles;
using Mapsui.Tiling;
using System;
using System.ComponentModel;

namespace AetherAprs.Views.Pages;

public partial class HomeView : UserControl
{
    private WritableLayer? _userLocationLayer;
    private PointFeature? _userLocationFeature;
    private MPoint? _lastUserMapPoint;

    public HomeView()
    {
        InitializeComponent();
        InitializeMap();

        DataContextChanged += OnDataContextChanged;
    }

    private void InitializeMap()
    {
        // Initialize the map with default OpenStreetMap tiles
        MapControl.Map = new Map();
        MapControl.Map.Layers.Add(OpenStreetMap.CreateTileLayer(), group: -1);

        // Create user location layer
        _userLocationLayer = new WritableLayer
        {
            Name = "User Location",
            Style = null // Style will be set on the feature
        };
        MapControl.Map.Layers.Add(_userLocationLayer, group: 1);
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is HomeViewModel viewModel)
        {
            // Add received beacons layer if available
            if (viewModel.ReceivedBeacons != null && MapControl.Map != null)
            {
                MapControl.Map.Layers.Add(viewModel.ReceivedBeacons.BeaconsLayer, group: 1);
            }

            // Subscribe to LocationTracking property changes
            if (viewModel.LocationTracking != null)
            {
                viewModel.LocationTracking.PropertyChanged += OnLocationTrackingPropertyChanged;
            }

            // Only start location tracking at runtime, not in designer
            if (!Design.IsDesignMode)
            {
                _ = viewModel.StartLocationTrackingAsync();
            }
        }
    }

    private void OnLocationTrackingPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not LocationTrackingViewModel locationTracking)
            return;

        // Update map when location changes
        if (e.PropertyName == nameof(LocationTrackingViewModel.CurrentLocation))
        {
            UpdateUserLocationOnMap(locationTracking.CurrentLocation);
        }
    }

    private void OnCenterOnUserClick(object? sender, RoutedEventArgs e)
    {
        if (_lastUserMapPoint is not null)
        {
            CenterOnUser(_lastUserMapPoint);
            return;
        }

        if (DataContext is HomeViewModel viewModel &&
            viewModel.LocationTracking?.CurrentLocation is { } location)
        {
            UpdateUserLocationOnMap(location);
            if (_lastUserMapPoint is not null)
            {
                CenterOnUser(_lastUserMapPoint);
            }
        }
    }

    private void UpdateUserLocationOnMap(LocationData? locationData)
    {
        if (_userLocationLayer == null || locationData == null)
            return;

        // Convert lat/lon to map coordinates (Web Mercator)
        var sphericalMercatorCoordinate = SphericalMercator.FromLonLat(locationData.Longitude, locationData.Latitude);
        var mapPoint = new MPoint(sphericalMercatorCoordinate.x, sphericalMercatorCoordinate.y);
        _lastUserMapPoint = mapPoint;

        // Create style for user location marker
        var locationStyle = new SymbolStyle
        {
            SymbolScale = 0.5,
            Fill = new Brush(Color.FromArgb(150, 0, 122, 255)),
            Outline = new Pen(Color.White, 2)
        };

        if (_userLocationFeature == null)
        {
            // Create new feature for user location
            _userLocationFeature = new PointFeature(mapPoint)
            {
                Styles = [locationStyle]
            };
            _userLocationLayer.Add(_userLocationFeature);

            // Center map on first location
            CenterOnUser(mapPoint);
        }
        else
        {
            // Clear and recreate feature at updated location
            _userLocationLayer.Clear();

            _userLocationFeature = new PointFeature(mapPoint)
            {
                Styles = [locationStyle]
            };
            _userLocationLayer.Add(_userLocationFeature);
            _userLocationLayer.DataHasChanged();
        }
    }

    private void CenterOnUser(MPoint mapPoint)
    {
        var navigator = MapControl.Map?.Navigator;
        if (navigator is null)
        {
            return;
        }

        // Prefer CenterOnAndZoomTo when resolutions are available (Mapsui sample style).
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
}
