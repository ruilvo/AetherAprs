// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Linq;

using Mapsui;
using Mapsui.Layers;

namespace AetherAprs.Services.UI;

/// <summary>
/// Turns a tap on the home map into the action the page should perform: open a
/// detail page, zoom into a cluster, or nothing. Hit testing lives here so the page
/// only maps the result onto navigation calls.
/// </summary>
public static class MapTapResolver
{
    /// <summary>Hit radius around the tap, in screen pixels.</summary>
    public const double TapTolerancePixels = 20;

    /// <summary>
    /// Finds the first navigable feature under <paramref name="worldPosition"/>.
    /// A cluster zooms in, an object or item opens its details, and a station opens
    /// the station page. Layers are queried in the order the map draws them, so the
    /// earliest layer holding a match decides.
    /// </summary>
    /// <param name="layers">The layers rendered by the map.</param>
    /// <param name="worldPosition">The tapped point in world coordinates.</param>
    /// <param name="resolution">The current viewport resolution (world units per pixel).</param>
    public static MapTapResult Resolve(
        IEnumerable<ILayer> layers,
        MPoint worldPosition,
        double resolution)
    {
        ArgumentNullException.ThrowIfNull(layers);

        double tolerance = resolution * TapTolerancePixels;

        MRect tapArea = new(
            worldPosition.X - tolerance,
            worldPosition.Y - tolerance,
            worldPosition.X + tolerance,
            worldPosition.Y + tolerance);

        foreach (ILayer layer in layers)
        {
            foreach (IFeature feature in layer.GetFeatures(tapArea, resolution))
            {
                if (TryResolveFeature(feature, resolution, out MapTapResult result))
                {
                    return result;
                }
            }
        }

        return MapTapResult.None.Instance;
    }

    private static bool TryResolveFeature(IFeature feature, double resolution, out MapTapResult result)
    {
        result = MapTapResult.None.Instance;

        if (feature.Fields is null)
        {
            return false;
        }

        // Cluster click: zoom in (animated), centered on the cluster centroid.
        if (feature.Fields.Contains("Cluster") && feature is PointFeature clusterFeature)
        {
            result = new MapTapResult.ZoomToCluster(clusterFeature.Point, resolution * 0.5);
            return true;
        }

        // Object/item click: open the object details page.
        if (feature.Fields.Contains("ObjectName"))
        {
            if (feature["ObjectName"] as string is { Length: > 0 } objectName)
            {
                result = new MapTapResult.OpenObject(objectName);
                return true;
            }
        }

        // Beacon click: open station details.
        if (feature.Fields.Contains("Callsign"))
        {
            if (feature["Callsign"] as string is { Length: > 0 } callsign)
            {
                result = new MapTapResult.OpenBeacon(callsign);
                return true;
            }
        }

        return false;
    }
}

/// <summary>What a tap on the map asked the page to do.</summary>
public abstract record MapTapResult
{
    private MapTapResult()
    {
    }

    /// <summary>The tap hit nothing navigable.</summary>
    public sealed record None : MapTapResult
    {
        /// <summary>The shared instance; the result carries no state.</summary>
        public static None Instance { get; } = new();
    }

    /// <summary>The tap hit a cluster; centre the map there at <paramref name="Resolution"/>.</summary>
    /// <param name="Center">The cluster centroid in world coordinates.</param>
    /// <param name="Resolution">The resolution to zoom to.</param>
    public sealed record ZoomToCluster(MPoint Center, double Resolution) : MapTapResult;

    /// <summary>The tap hit an object or item.</summary>
    /// <param name="Name">The object name shown on its details page.</param>
    public sealed record OpenObject(string Name) : MapTapResult;

    /// <summary>The tap hit a station.</summary>
    /// <param name="Callsign">The station callsign.</param>
    public sealed record OpenBeacon(string Callsign) : MapTapResult;
}
