// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Services.UI;
using Mapsui;
using Mapsui.Projections;
using Xunit;

namespace AetherAprs.Tests.Services.UI;

public class MapSelectionDragTests
{
    [Fact]
    public void Complete_TapWithoutDistance_SelectsNothing()
    {
        MPoint point = new(1000, 2000);

        MapSelection? selection = MapSelectionDrag.Complete(point, new MPoint(1000, 2000));

        Assert.Null(selection);
    }

    [Fact]
    public void Complete_DragFromSouthWestToNorthEast_ReturnsOrderedBounds()
    {
        // Drag upwards and to the right on the screen: world Y grows northwards in
        // Mercator, so this drag selects the north-east rectangle from the origin.
        MPoint start = new(SphericalMercator.FromLonLat(-8.5, 40.5).x, SphericalMercator.FromLonLat(-8.5, 40.5).y);
        MPoint end = new(SphericalMercator.FromLonLat(-8.0, 41.0).x, SphericalMercator.FromLonLat(-8.0, 41.0).y);

        MapSelection selection = Assert.IsType<MapSelection>(MapSelectionDrag.Complete(start, end));

        Assert.Equal(-8.5, selection.MinLongitude, 6);
        Assert.Equal(40.5, selection.MinLatitude, 6);
        Assert.Equal(-8.0, selection.MaxLongitude, 6);
        Assert.Equal(41.0, selection.MaxLatitude, 6);
    }

    [Fact]
    public void Complete_ReversedDrag_StillReturnsMinThenMax()
    {
        MPoint southWest = new(SphericalMercator.FromLonLat(-9.0, 39.0).x, SphericalMercator.FromLonLat(-9.0, 39.0).y);
        MPoint northEast = new(SphericalMercator.FromLonLat(-8.0, 40.0).x, SphericalMercator.FromLonLat(-8.0, 40.0).y);

        MapSelection selection = Assert.IsType<MapSelection>(MapSelectionDrag.Complete(northEast, southWest));

        Assert.Equal(-9.0, selection.MinLongitude, 6);
        Assert.Equal(39.0, selection.MinLatitude, 6);
        Assert.Equal(-8.0, selection.MaxLongitude, 6);
        Assert.Equal(40.0, selection.MaxLatitude, 6);
    }
}
