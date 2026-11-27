// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using Mapsui.Layers;

namespace AetherAprs.Services.UI;

/// <summary>
/// Creates the base map layer (the tile layer drawn underneath all overlays) based
/// on the configured <see cref="Configuration.Settings.MapSettings.TileSource"/>.
/// </summary>
public interface IMapTileSourceService
{
    /// <summary>
    /// The outcome of the last <see cref="CreateBaseMapLayer"/> call. Callers use it to
    /// tell the operator when a configured offline package was rejected and the map fell
    /// back to the online source.
    /// </summary>
    MapTileSourceStatus Status { get; }

    /// <summary>
    /// Creates a new base map layer using the currently configured tile source.
    /// </summary>
    /// <returns>An online OpenStreetMap layer or an offline MBTiles layer.</returns>
    ILayer CreateBaseMapLayer();
}
