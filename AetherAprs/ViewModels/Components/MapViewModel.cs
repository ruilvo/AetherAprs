// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.ComponentModel;
using AetherAprs.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Projections;
using Mapsui.Styles;

namespace AetherAprs.ViewModels.Components;

/// <summary>
/// ViewModel for map display and user location management.
/// Separates map state/behavior from View implementation details.
/// </summary>
public partial class MapViewModel : ViewModelBase, IDisposable
{
    private MPoint? _lastUserMapPoint;
    private bool _disposed;

    [ObservableProperty]
    public partial LocationData? UserLocation { get; set; }

    [ObservableProperty]
    public partial WritableLayer? UserLocationLayer { get; set; }

    public event EventHandler<MPoint>? CenterOnPointRequested;

    public MapViewModel()
    {
        UserLocationLayer = new WritableLayer
        {
            Name = "User Location",
            Style = null
        };
    }

    /// <summary>
    /// Updates the user location marker on the map.
    /// </summary>
    public void UpdateUserLocation(LocationData? location)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (location == null || UserLocationLayer == null)
        {
            return;
        }

        UserLocation = location;

        // Convert lat/lon to map coordinates (Web Mercator)
        var sphericalMercatorCoordinate = SphericalMercator.FromLonLat(location.Longitude, location.Latitude);
        var mapPoint = new MPoint(sphericalMercatorCoordinate.x, sphericalMercatorCoordinate.y);
        _lastUserMapPoint = mapPoint;

        // Create style for user location marker
        var locationStyle = new SymbolStyle
        {
            SymbolScale = 0.5,
            Fill = new Brush(Color.FromArgb(150, 0, 122, 255)),
            Outline = new Pen(Color.White, 2)
        };

        // Clear and recreate feature at updated location
        UserLocationLayer.Clear();
        var feature = new PointFeature(mapPoint)
        {
            Styles = [locationStyle]
        };
        UserLocationLayer.Add(feature);
        UserLocationLayer.DataHasChanged();
    }

    [RelayCommand]
    public void CenterOnUser()
    {
        if (_lastUserMapPoint != null)
        {
            CenterOnPointRequested?.Invoke(this, _lastUserMapPoint);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        var layer = UserLocationLayer;
        UserLocationLayer = null;

        layer?.Clear();
    }
}
