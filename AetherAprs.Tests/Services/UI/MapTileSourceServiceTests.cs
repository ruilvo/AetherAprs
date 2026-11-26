// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Configuration;
using AetherAprs.Configuration.Settings;
using AetherAprs.Services.Configuration;
using AetherAprs.Services.UI;
using AetherAprs.Tests.Helpers;
using Mapsui.Layers;
using Mapsui.Tiling.Layers;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using System;
using System.IO;
using Xunit;

namespace AetherAprs.Tests.Services.UI;

public sealed class MapTileSourceServiceTests
{
    [Fact]
    public void CreateBaseMapLayer_OsmOnline_ReturnsOsmLayer()
    {
        MapTileSourceService service = CreateService(TileSourceType.OsmOnline, string.Empty);

        ILayer layer = service.CreateBaseMapLayer();

        Assert.Equal("OpenStreetMap", layer.Name);
    }

    [Fact]
    public void CreateBaseMapLayer_MbTilesWithValidFile_ReturnsMbTilesLayer()
    {
        string directory = MbTilesTestFixture.CreateTemporaryDirectory();
        try
        {
            string path = MbTilesTestFixture.Create(directory);
            MapTileSourceService service = CreateService(TileSourceType.MbTilesOffline, path);

            ILayer layer = service.CreateBaseMapLayer();

            TileLayer tileLayer = Assert.IsType<TileLayer>(layer);
            Assert.IsType<MbTilesTileSource>(tileLayer.TileSource);
            Assert.Equal("Test Map", layer.Name);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void CreateBaseMapLayer_MbTilesWithMissingFile_FallsBackToOsm()
    {
        string missingPath = Path.Combine(Path.GetTempPath(), $"does-not-exist-{Guid.NewGuid():N}.mbtiles");
        MapTileSourceService service = CreateService(TileSourceType.MbTilesOffline, missingPath);

        ILayer layer = service.CreateBaseMapLayer();

        Assert.Equal("OpenStreetMap", layer.Name);
    }

    [Fact]
    public void CreateBaseMapLayer_MbTilesWithEmptyPath_FallsBackToOsm()
    {
        MapTileSourceService service = CreateService(TileSourceType.MbTilesOffline, string.Empty);

        ILayer layer = service.CreateBaseMapLayer();

        Assert.Equal("OpenStreetMap", layer.Name);
    }

    private static MapTileSourceService CreateService(TileSourceType tileSource, string mbTilesFilePath)
    {
        IConfigurationService configuration = Substitute.For<IConfigurationService>();
        configuration.Current.Returns(new AppSettings
        {
            Map = new MapSettings
            {
                TileSource = tileSource,
                MbTilesFilePath = mbTilesFilePath
            }
        });

        return new MapTileSourceService(configuration, NullLogger<MapTileSourceService>.Instance);
    }
}
