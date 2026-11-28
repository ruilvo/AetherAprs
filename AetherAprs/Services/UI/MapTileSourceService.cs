// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.IO;

using AetherAprs.Configuration;
using AetherAprs.Configuration.Settings;
using AetherAprs.Services.Configuration;

using Mapsui.Layers;
using Mapsui.Tiling;
using Mapsui.Tiling.Layers;

using Microsoft.Extensions.Logging;

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
    private readonly Func<string, ILayer> _createOnlineLayer;

    /// <summary>
    /// Initializes a new instance of the <see cref="MapTileSourceService"/> class.
    /// </summary>
    public MapTileSourceService(
        IConfigurationService configurationService,
        ILogger<MapTileSourceService> logger)
        : this(configurationService, logger, OpenStreetMap.CreateTileLayer)
    {
    }

    internal MapTileSourceService(
        IConfigurationService configurationService,
        ILogger<MapTileSourceService> logger,
        Func<string, ILayer> createOnlineLayer)
    {
        _configurationService = configurationService ?? throw new ArgumentNullException(nameof(configurationService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _createOnlineLayer = createOnlineLayer ?? throw new ArgumentNullException(nameof(createOnlineLayer));
    }

    /// <inheritdoc />
    public MapTileSourceStatus Status { get; private set; } =
        new(TileSourceType.OsmOnline, MapTileSourceFallbackReason.None, null);

    /// <inheritdoc />
    public ILayer CreateBaseMapLayer()
    {
        MapSettings map = _configurationService.Current.Map;

        if (map.TileSource != TileSourceType.MbTilesOffline)
        {
            Status = new MapTileSourceStatus(TileSourceType.OsmOnline, MapTileSourceFallbackReason.None, null);
            return CreateOsmLayer();
        }

        if (string.IsNullOrWhiteSpace(map.MbTilesFilePath))
        {
            _logger.LogWarning(
                "Offline MBTiles was selected without a package path; falling back to online OpenStreetMap.");
            return FallBack(MapTileSourceFallbackReason.NotConfigured, null);
        }

        if (!File.Exists(map.MbTilesFilePath))
        {
            _logger.LogWarning(
                "Configured MBTiles file does not exist; falling back to online OpenStreetMap. Path: {Path}",
                map.MbTilesFilePath);
            return FallBack(MapTileSourceFallbackReason.FileMissing, map.MbTilesFilePath);
        }

        try
        {
            MbTilesTileSource source = new(map.MbTilesFilePath);
            TileLayer layer = new(source)
            {
                Name = string.IsNullOrEmpty(source.Name) ? "MBTiles" : source.Name
            };
            Status = new MapTileSourceStatus(TileSourceType.MbTilesOffline, MapTileSourceFallbackReason.None, map.MbTilesFilePath);
            return layer;
        }
        catch (CorruptTilePackageException ex)
        {
            _logger.LogError(ex,
                "Configured MBTiles package is corrupt; falling back to online OpenStreetMap. Path: {Path}, Fault: {Fault}",
                map.MbTilesFilePath,
                ex.Fault);
            return FallBack(MapTileSourceFallbackReason.PackageCorrupt, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to open MBTiles file; falling back to online OpenStreetMap. Path: {Path}",
                map.MbTilesFilePath);
            return FallBack(MapTileSourceFallbackReason.PackageCorrupt, ex.Message);
        }
    }

    private ILayer FallBack(MapTileSourceFallbackReason reason, string? detail)
    {
        Status = new MapTileSourceStatus(TileSourceType.OsmOnline, reason, detail);
        return CreateOsmLayer();
    }

    private ILayer CreateOsmLayer() => _createOnlineLayer(OsmUserAgent);
}
