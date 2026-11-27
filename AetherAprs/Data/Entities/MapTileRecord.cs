// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;

namespace AetherAprs.Data.Entities;

/// <summary>
/// One cached map tile. The cache is disposable state: losing a row only costs a
/// re-download, so rows are inserted idempotently and pruned by age.
/// </summary>
public sealed class MapTileRecord
{
    /// <summary>Web mercator zoom level.</summary>
    public int ZoomLevel { get; set; }

    /// <summary>Tile column in the XYZ scheme.</summary>
    public int TileColumn { get; set; }

    /// <summary>Tile row in the XYZ scheme (the scheme the tile provider serves).</summary>
    public int TileRow { get; set; }

    /// <summary>Encoded tile image bytes.</summary>
    public byte[] Data { get; set; } = [];

    /// <summary>UTC instant the tile was stored, used to prune the oldest tiles.</summary>
    public DateTime UpdatedAt { get; set; }
}
