// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Models;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Projections;
using Mapsui.Styles;
using Microsoft.Extensions.Logging;
using System;

namespace AetherAprs.Services.UI;

/// <summary>
/// Service that provides a map layer for displaying the user's current location.
/// </summary>
public interface IUserLocationLayerService : IDisposable
{
    /// <summary>
    /// Gets the map layer containing the user location marker.
    /// </summary>
    ILayer Layer { get; }

    /// <summary>
    /// Gets the current user location in map coordinates, or null if not set.
    /// </summary>
    MPoint? CurrentMapPoint { get; }

    /// <summary>
    /// Updates the user location marker on the map.
    /// </summary>
    void UpdateLocation(LocationData? location);
}

/// <summary>
/// Implementation of user location layer service.
/// </summary>
public sealed class UserLocationLayerService : IUserLocationLayerService
{
    private readonly ILogger<UserLocationLayerService> _logger;
    private readonly WritableLayer _layer;
    private MPoint? _currentMapPoint;
    private bool _disposed;

    public ILayer Layer => _layer;
    public MPoint? CurrentMapPoint => _currentMapPoint;

    public UserLocationLayerService(ILogger<UserLocationLayerService> logger)
    {
        _logger = logger;
        _layer = new WritableLayer
        {
            Name = "User Location",
            Style = null
        };
    }

    public void UpdateLocation(LocationData? location)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (location == null)
        {
            return;
        }

        var (x, y) = SphericalMercator.FromLonLat(location.Location.Longitude, location.Location.Latitude);
        var mapPoint = new MPoint(x, y);
        _currentMapPoint = mapPoint;

        var locationStyle = new SymbolStyle
        {
            SymbolScale = 0.5,
            Fill = new Brush(Color.FromArgb(150, 0, 122, 255)),
            Outline = new Pen(Color.White, 2)
        };

        _layer.Clear();
        var feature = new PointFeature(mapPoint)
        {
            Styles = [locationStyle]
        };
        _layer.Add(feature);
        _layer.DataHasChanged();

        _logger.LogDebug("User location updated on map: {Lat}, {Lon}", location.Location.Latitude, location.Location.Longitude);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _layer.Clear();
    }
}
