// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using Mapsui;
using Mapsui.Projections;
using System;

namespace AetherAprs.Services.UI;

/// <summary>
/// Converts a completed drag on the offline-map page into the geographic rectangle
/// the operator selected, so the page only forwards pointer events.
/// </summary>
public static class MapSelectionDrag
{
    /// <summary>
    /// Builds the selection for a drag that ended at <paramref name="end"/>. A tap
    /// (no distance between the two points) selects nothing.
    /// </summary>
    /// <param name="start">Where the drag began, in world coordinates.</param>
    /// <param name="end">Where the drag ended, in world coordinates.</param>
    /// <returns>The selected bounds, or <see langword="null"/> for a tap.</returns>
    public static MapSelection? Complete(MPoint start, MPoint end)
    {
        ArgumentNullException.ThrowIfNull(start);
        ArgumentNullException.ThrowIfNull(end);

        double minX = Math.Min(start.X, end.X);
        double minY = Math.Min(start.Y, end.Y);
        double maxX = Math.Max(start.X, end.X);
        double maxY = Math.Max(start.Y, end.Y);

        if (maxX - minX <= double.Epsilon || maxY - minY <= double.Epsilon)
        {
            return null;
        }

        (double minLongitude, double minLatitude) = SphericalMercator.ToLonLat(minX, minY);
        (double maxLongitude, double maxLatitude) = SphericalMercator.ToLonLat(maxX, maxY);

        return new MapSelection(minLongitude, minLatitude, maxLongitude, maxLatitude);
    }
}

/// <summary>A geographic rectangle selected on the map.</summary>
/// <param name="MinLongitude">Western edge, in degrees.</param>
/// <param name="MinLatitude">Southern edge, in degrees.</param>
/// <param name="MaxLongitude">Eastern edge, in degrees.</param>
/// <param name="MaxLatitude">Northern edge, in degrees.</param>
public sealed record MapSelection(
    double MinLongitude,
    double MinLatitude,
    double MaxLongitude,
    double MaxLatitude);
