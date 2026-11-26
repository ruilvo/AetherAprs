// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

namespace AetherAprs.Configuration.Settings;

/// <summary>
/// Map display settings, including the base map tile source.
/// </summary>
public sealed record MapSettings
{
    /// <summary>
    /// Gets the base map tile source.
    /// Default is <see cref="TileSourceType.OsmOnline"/>.
    /// </summary>
    public TileSourceType TileSource { get; init; } = TileSourceType.OsmOnline;

    /// <summary>
    /// Gets the path to the MBTiles package file used when
    /// <see cref="TileSource"/> is <see cref="TileSourceType.MbTilesOffline"/>.
    /// </summary>
    public string MbTilesFilePath { get; init; } = string.Empty;
}
