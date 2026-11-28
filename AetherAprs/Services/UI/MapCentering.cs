// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;

using Mapsui;

namespace AetherAprs.Services.UI;

/// <summary>
/// Computes the viewport a "center on my location" request should animate to.
/// </summary>
/// <remarks>
/// Centering, zooming and resetting the rotation are combined into a single
/// viewport so the map can animate them together. Mapsui keeps one animation per
/// navigator, so issuing <c>CenterOnAndZoomTo</c> and then <c>RotateTo</c> as two
/// separate calls makes the rotation animation replace the centering one. That
/// left the map rotating to north without actually recentering or zooming on the
/// operator's position.
/// </remarks>
public static class MapCentering
{
    /// <summary>Zoom level (index into the map resolutions) a centering request targets.</summary>
    public const int PreferredResolutionIndex = 9;

    /// <summary>Resolution used when the map exposes too few resolution levels.</summary>
    public const double FallbackResolution = 2000;

    /// <summary>
    /// Selects the resolution a centering request should zoom to.
    /// </summary>
    /// <param name="resolutions">The map's available resolutions, coarsest first.</param>
    /// <returns>
    /// The preferred resolution, or <see cref="FallbackResolution"/> when the map
    /// exposes fewer levels than <see cref="PreferredResolutionIndex"/>.
    /// </returns>
    public static double SelectResolution(IReadOnlyList<double> resolutions)
    {
        ArgumentNullException.ThrowIfNull(resolutions);

        return resolutions.Count > PreferredResolutionIndex
            ? resolutions[PreferredResolutionIndex]
            : FallbackResolution;
    }

    /// <summary>
    /// Builds the target viewport that centers on the given point, zooms to a usable
    /// resolution and resets the rotation to north in one step.
    /// </summary>
    /// <param name="current">The current viewport.</param>
    /// <param name="point">The world point to center on.</param>
    /// <param name="resolutions">The map's available resolutions.</param>
    /// <returns>The viewport to animate to.</returns>
    public static Viewport CreateCenteredViewport(
        Viewport current,
        MPoint point,
        IReadOnlyList<double> resolutions) =>
        current with
        {
            CenterX = point.X,
            CenterY = point.Y,
            Resolution = SelectResolution(resolutions),
            Rotation = 0
        };
}
