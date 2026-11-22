// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Models;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Nts;
using Mapsui.Projections;
using Mapsui.Styles;
using Microsoft.Extensions.Logging;
using NetTopologySuite.Geometries;
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

        _layer.Clear();

        // Draw base circle for location
        var locationStyle = new SymbolStyle
        {
            SymbolScale = 0.5,
            Fill = new Brush(Color.FromArgb(150, 0, 122, 255)),
            Outline = new Pen(Color.White, 2)
        };

        var locationFeature = new PointFeature(mapPoint)
        {
            Styles = [locationStyle]
        };
        _layer.Add(locationFeature);

        // Draw directional indicator if bearing is available
        if (location.Bearing.HasValue)
        {
            var directionArrow = CreateDirectionArrow(mapPoint, location.Bearing.Value);
            _layer.Add(directionArrow);
        }

        _layer.DataHasChanged();

        _logger.LogDebug("User location updated on map: {Lat}, {Lon}, Bearing: {Bearing}", 
            location.Location.Latitude, location.Location.Longitude, location.Bearing);
    }

    private static GeometryFeature CreateDirectionArrow(MPoint center, double bearingDegrees)
    {
        // Create a triangle pointing north (0 degrees), then rotate it
        // Triangle size is in map units (meters in Web Mercator)
        const double arrowSize = 15; // meters
        const double arrowWidth = 8; // meters

        // Triangle vertices (pointing up/north before rotation)
        var vertices = new[]
        {
            new Coordinate(center.X, center.Y + arrowSize), // tip (north)
            new Coordinate(center.X - arrowWidth, center.Y - arrowSize / 2), // left base
            new Coordinate(center.X + arrowWidth, center.Y - arrowSize / 2), // right base
            new Coordinate(center.X, center.Y + arrowSize) // close the ring
        };

        // Create polygon
        var geometryFactory = new GeometryFactory();
        var shell = geometryFactory.CreateLinearRing(vertices);
        var polygon = geometryFactory.CreatePolygon(shell);

        // Rotate the polygon based on bearing
        // Mapsui uses counter-clockwise rotation, bearing is clockwise from north
        var rotationRadians = bearingDegrees * Math.PI / 180.0;
        var transform = new NetTopologySuite.Geometries.Utilities.AffineTransformation();
        transform.Rotate(rotationRadians, center.X, center.Y);
        var rotatedPolygon = (Polygon)transform.Transform(polygon);

        var arrowStyle = new VectorStyle
        {
            Fill = new Brush(Color.FromArgb(200, 255, 255, 255)),
            Outline = new Pen(Color.FromArgb(255, 0, 122, 255), 2)
        };

        var arrowFeature = new GeometryFeature
        {
            Geometry = rotatedPolygon,
            Styles = [arrowStyle]
        };

        return arrowFeature;
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
