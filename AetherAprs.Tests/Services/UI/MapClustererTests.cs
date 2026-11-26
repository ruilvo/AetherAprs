// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Services.UI;
using Mapsui;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace AetherAprs.Tests.Services.UI;

public sealed class MapClustererTests
{
    [Fact]
    public void Cluster_Empty_ReturnsEmpty()
    {
        IReadOnlyList<IReadOnlyList<int>> groups = MapClusterer.Cluster([], cellSize: 100);

        Assert.Empty(groups);
    }

    [Fact]
    public void Cluster_SinglePoint_ReturnsSingleGroupOfOne()
    {
        IReadOnlyList<IReadOnlyList<int>> groups = MapClusterer.Cluster([new MPoint(10, 20)], cellSize: 100);

        IReadOnlyList<int> group = Assert.Single(groups);
        Assert.Equal([0], group);
    }

    [Fact]
    public void Cluster_PointsInSameCell_AreGrouped()
    {
        // Both points land in cell (0, 0) for a cell size of 100.
        MPoint[] points = [new MPoint(10, 10), new MPoint(20, 30)];

        IReadOnlyList<IReadOnlyList<int>> groups = MapClusterer.Cluster(points, cellSize: 100);

        IReadOnlyList<int> group = Assert.Single(groups);
        Assert.Equal(2, group.Count);
        Assert.Contains(0, group);
        Assert.Contains(1, group);
    }

    [Fact]
    public void Cluster_PointsInDifferentCells_AreSeparate()
    {
        // (50, 50) -> cell (0, 0); (150, 150) -> cell (1, 1) for cell size 100.
        MPoint[] points = [new MPoint(50, 50), new MPoint(150, 150)];

        IReadOnlyList<IReadOnlyList<int>> groups = MapClusterer.Cluster(points, cellSize: 100);

        Assert.Equal(2, groups.Count);
        Assert.All(groups, g => Assert.Single(g));
    }

    [Fact]
    public void Cluster_NegativeCoordinates_AreGroupedByFloor()
    {
        // (-10, -10) -> cell (-1, -1); (-20, -20) -> cell (-1, -1).
        MPoint[] points = [new MPoint(-10, -10), new MPoint(-20, -20)];

        IReadOnlyList<IReadOnlyList<int>> groups = MapClusterer.Cluster(points, cellSize: 100);

        IReadOnlyList<int> group = Assert.Single(groups);
        Assert.Equal(2, group.Count);
    }

    [Fact]
    public void Cluster_NonPositiveCellSize_Throws() => Assert.Throws<ArgumentOutOfRangeException>(() =>
                                                                 MapClusterer.Cluster([new MPoint(1, 1)], cellSize: 0));
}
