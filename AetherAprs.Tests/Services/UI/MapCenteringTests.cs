// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Collections.Generic;
using System.Linq;

using AetherAprs.Services.UI;

using Mapsui;

using Xunit;

namespace AetherAprs.Tests.Services.UI;

public sealed class MapCenteringTests
{
    private static List<double> Resolutions(int count) =>
        [.. Enumerable.Range(0, count).Select(index => 1000.0 - index)];

    [Fact]
    public void SelectResolution_PrefersConfiguredLevel()
    {
        List<double> resolutions = Resolutions(12);

        Assert.Equal(resolutions[MapCentering.PreferredResolutionIndex], MapCentering.SelectResolution(resolutions));
    }

    [Fact]
    public void SelectResolution_FallsBack_WhenTooFewLevels()
    {
        List<double> resolutions = Resolutions(3);

        Assert.Equal(MapCentering.FallbackResolution, MapCentering.SelectResolution(resolutions));
    }

    [Fact]
    public void CreateCenteredViewport_CentersZoomsAndResetsRotation()
    {
        // A rotated, zoomed-out viewport must become north-up, recentered and zoomed
        // in, all in the same viewport update so a single animation can apply it.
        List<double> resolutions = Resolutions(12);
        Viewport current = new()
        {
            CenterX = 1,
            CenterY = 2,
            Resolution = 999,
            Rotation = 45,
            Width = 800,
            Height = 600
        };
        MPoint target = new(500, 600);

        Viewport result = MapCentering.CreateCenteredViewport(current, target, resolutions);

        Assert.Equal(500, result.CenterX);
        Assert.Equal(600, result.CenterY);
        Assert.Equal(resolutions[MapCentering.PreferredResolutionIndex], result.Resolution);
        Assert.Equal(0, result.Rotation);
        Assert.Equal(800, result.Width);
        Assert.Equal(600, result.Height);
    }
}
