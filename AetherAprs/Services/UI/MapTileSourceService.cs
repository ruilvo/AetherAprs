// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Configuration;
using AetherAprs.Configuration.Settings;
using AetherAprs.Services.Configuration;
using Mapsui.Layers;
using Mapsui.Tiling;
using Mapsui.Tiling.Layers;
using Microsoft.Extensions.Logging;
using System;
using System.IO;

namespace AetherAprs.Services.UI;

/// <summary>
/// Default implementation of <see cref="IMapTileSourceService"/>. Selects between the
/// online OpenStreetMap source and an offline MBTiles package based on settings.
/// </summary>
public sealed class MapTileSourceService(
    IConfigurationService configurationService,
    ILogger<MapTileSourceService> logger) : IMapTileSourceService
{
    // OSM requires an identifiable User-Agent; generic defaults get blocked.
    private const string OsmUserAgent =
        "AetherAprs/1.0 (+https://github.com/ruilvo/AetherAprs)";

    private readonly IConfigurationService _configurationService = configurationService;
    private readonly ILogger<MapTileSourceService> _logger = logger;

    /// <inheritdoc />
    public ILayer CreateBaseMapLayer()
    {
        MapSettings map = _configurationService.Current.Map;

        if (map.TileSource == TileSourceType.MbTilesOffline &&
            !string.IsNullOrWhiteSpace(map.MbTilesFilePath))
        {
            if (!File.Exists(map.MbTilesFilePath))
            {
                _logger.LogWarning(
                    "Configured MBTiles file does not exist; falling back to online OpenStreetMap. Path: {Path}",
                    map.MbTilesFilePath);
                return CreateOsmLayer();
            }

            try
            {
                MbTilesTileSource source = new(map.MbTilesFilePath);
                TileLayer layer = new(source)
                {
                    Name = string.IsNullOrEmpty(source.Name) ? "MBTiles" : source.Name
                };
                return layer;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to open MBTiles file; falling back to online OpenStreetMap. Path: {Path}",
                    map.MbTilesFilePath);
                return CreateOsmLayer();
            }
        }

        return CreateOsmLayer();
    }

    private static TileLayer CreateOsmLayer() => OpenStreetMap.CreateTileLayer(OsmUserAgent);
}
