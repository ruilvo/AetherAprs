// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Configuration;
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
public sealed class MapTileSourceService : IMapTileSourceService
{
    // OSM requires an identifiable User-Agent; generic defaults get blocked.
    private const string OsmUserAgent =
        "AetherAprs/1.0 (+https://github.com/ruilvo/AetherAprs)";

    private readonly IConfigurationService _configurationService;
    private readonly ILogger<MapTileSourceService> _logger;

    public MapTileSourceService(
        IConfigurationService configurationService,
        ILogger<MapTileSourceService> logger)
    {
        _configurationService = configurationService;
        _logger = logger;
    }

    /// <inheritdoc />
    public ILayer CreateBaseMapLayer()
    {
        var map = _configurationService.GetSettingsSnapshot().Map;

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
                var source = new MbTilesTileSource(map.MbTilesFilePath);
                var layer = new TileLayer(source);
                layer.Name = string.IsNullOrEmpty(source.Name) ? "MBTiles" : source.Name;
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
