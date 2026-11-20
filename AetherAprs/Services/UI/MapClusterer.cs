// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using Mapsui;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AetherAprs.Services.UI;

/// <summary>
/// Groups map points into a regular grid for marker clustering. Points that land in the
/// same grid cell are returned as a single group of indices into the input collection.
/// </summary>
public static class MapClusterer
{
    /// <summary>
    /// Groups points into grid cells of the given size (in world units).
    /// Each returned group holds the indices of the points that share a cell.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<int>> Cluster(IReadOnlyList<MPoint> points, double cellSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(cellSize);

        var groups = new Dictionary<(long CellX, long CellY), List<int>>();

        for (var i = 0; i < points.Count; i++)
        {
            var point = points[i];
            var cell = (
                CellX: (long)Math.Floor(point.X / cellSize),
                CellY: (long)Math.Floor(point.Y / cellSize));

            if (!groups.TryGetValue(cell, out var indices))
            {
                indices = [];
                groups[cell] = indices;
            }

            indices.Add(i);
        }

        return groups.Values
            .Select(g => (IReadOnlyList<int>)g)
            .ToList();
    }
}
