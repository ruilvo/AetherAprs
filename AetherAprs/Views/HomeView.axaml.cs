// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later
using AetherAprs.Models;
using AetherAprs.ViewModels;
using Avalonia.Controls;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Projections;
using Mapsui.Styles;
using Mapsui.Tiling;
using System;
using System.ComponentModel;

namespace AetherAprs.Views;

public partial class HomeView : UserControl
{
    private WritableLayer? _userLocationLayer;
    private PointFeature? _userLocationFeature;

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
            viewModel.PropertyChanged += OnViewModelPropertyChanged;
            
            // Only start location tracking at runtime, not in designer
            if (!Design.IsDesignMode)
            {
                _ = viewModel.StartLocationTrackingAsync();
            }
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not HomeViewModel viewModel)
            return;

        // Update map when location changes
        if (e.PropertyName == nameof(HomeViewModel.UserLocation))
        {
            UpdateUserLocationOnMap(viewModel.UserLocation);
        }
    }

    private void UpdateUserLocationOnMap(LocationData? locationData)
    {
        if (_userLocationLayer == null || locationData == null)
            return;

        // Convert lat/lon to map coordinates (Web Mercator)
        var sphericalMercatorCoordinate = SphericalMercator.FromLonLat(locationData.Longitude, locationData.Latitude);
        var mapPoint = new MPoint(sphericalMercatorCoordinate.x, sphericalMercatorCoordinate.y);

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
            MapControl.Map?.Navigator?.CenterOn(mapPoint);
            MapControl.Map?.Navigator?.ZoomTo(2000); // Zoom to ~2km scale
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
}
