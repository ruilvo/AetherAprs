// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Services.UI;

using Mapsui;
using Mapsui.Layers;

using Xunit;

namespace AetherAprs.Tests.Services.UI;

public class MapTapResolverTests
{
    private const double Resolution = 4;

    [Fact]
    public void Resolve_EmptyLayers_ReturnsNone()
    {
        MapTapResult result = MapTapResolver.Resolve([], new MPoint(10, 10), Resolution);

        Assert.IsType<MapTapResult.None>(result);
    }

    [Fact]
    public void Resolve_TapOnCluster_ReturnsClusterZoomAtHalfResolution()
    {
        WritableLayer layer = new();
        PointFeature cluster = new(new MPoint(100, 100));
        cluster["Cluster"] = true;
        cluster["Count"] = 3;
        layer.Add(cluster);

        MapTapResult result = MapTapResolver.Resolve([layer], new MPoint(101, 101), Resolution);

        MapTapResult.ZoomToCluster zoom = Assert.IsType<MapTapResult.ZoomToCluster>(result);
        Assert.Equal(100, zoom.Center.X);
        Assert.Equal(100, zoom.Center.Y);
        Assert.Equal(Resolution / 2, zoom.Resolution);
    }

    [Fact]
    public void Resolve_TapOnObject_ReturnsOpenObject()
    {
        WritableLayer layer = new();
        PointFeature station = new(new MPoint(200, 200));
        station["ObjectName"] = "WX-STATION";
        station["Callsign"] = "K0OBJ-1";
        layer.Add(station);

        MapTapResult result = MapTapResolver.Resolve([layer], new MPoint(200, 200), Resolution);

        MapTapResult.OpenObject open = Assert.IsType<MapTapResult.OpenObject>(result);
        Assert.Equal("WX-STATION", open.Name);
    }

    [Fact]
    public void Resolve_TapOnStation_ReturnsOpenBeacon()
    {
        WritableLayer layer = new();
        PointFeature station = new(new MPoint(300, 300));
        station["Callsign"] = "K0STA-9";
        layer.Add(station);

        MapTapResult result = MapTapResolver.Resolve([layer], new MPoint(300, 300), Resolution);

        MapTapResult.OpenBeacon open = Assert.IsType<MapTapResult.OpenBeacon>(result);
        Assert.Equal("K0STA-9", open.Callsign);
    }

    [Fact]
    public void Resolve_TapOutsideTolerance_ReturnsNone()
    {
        WritableLayer layer = new();
        PointFeature station = new(new MPoint(400, 400));
        station["Callsign"] = "K0FAR-1";
        layer.Add(station);

        // Ten times the hit radius away from the marker.
        MapTapResult result = MapTapResolver.Resolve(
            [layer],
            new MPoint(400 + (MapTapResolver.TapTolerancePixels * Resolution * 10), 400),
            Resolution);

        Assert.IsType<MapTapResult.None>(result);
    }

    [Fact]
    public void Resolve_EmptyCallsign_ReturnsNone()
    {
        WritableLayer layer = new();
        PointFeature station = new(new MPoint(500, 500));
        station["Callsign"] = string.Empty;
        layer.Add(station);

        MapTapResult result = MapTapResolver.Resolve([layer], new MPoint(500, 500), Resolution);

        Assert.IsType<MapTapResult.None>(result);
    }

    [Fact]
    public void Resolve_FirstLayerWithAMatchDecides()
    {
        WritableLayer trails = new();
        PointFeature trailStation = new(new MPoint(600, 600));
        trailStation["Callsign"] = "K0TRAIL-1";
        trails.Add(trailStation);

        WritableLayer markers = new();
        PointFeature marker = new(new MPoint(600, 600));
        marker["Callsign"] = "K0MARK-1";
        markers.Add(marker);

        MapTapResult result = MapTapResolver.Resolve([trails, markers], new MPoint(600, 600), Resolution);

        MapTapResult.OpenBeacon open = Assert.IsType<MapTapResult.OpenBeacon>(result);
        Assert.Equal("K0TRAIL-1", open.Callsign);
    }
}
