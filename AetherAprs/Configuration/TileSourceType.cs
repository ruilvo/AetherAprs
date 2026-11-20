// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

namespace AetherAprs.Configuration;

/// <summary>
/// Selects the base map tile source rendered by the map.
/// </summary>
public enum TileSourceType
{
    /// <summary>
    /// Online OpenStreetMap tiles (requires internet).
    /// </summary>
    OsmOnline,

    /// <summary>
    /// Offline tiles served from an MBTiles package file.
    /// </summary>
    MbTilesOffline
}
