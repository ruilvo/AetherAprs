// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Configuration;
using AetherAprs.Configuration.Settings;
using AetherAprs.Services.Configuration;
using AetherAprs.Services.UI;
using AetherAprs.Tests.Helpers;
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
        var service = CreateService(TileSourceType.OsmOnline, string.Empty);

        var layer = service.CreateBaseMapLayer();

        Assert.Equal("OpenStreetMap", layer.Name);
    }

    [Fact]
    public void CreateBaseMapLayer_MbTilesWithValidFile_ReturnsMbTilesLayer()
    {
        var directory = MbTilesTestFixture.CreateTemporaryDirectory();
        try
        {
            var path = MbTilesTestFixture.Create(directory);
            var service = CreateService(TileSourceType.MbTilesOffline, path);

            var layer = service.CreateBaseMapLayer();

            var tileLayer = Assert.IsType<TileLayer>(layer);
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
        var missingPath = Path.Combine(Path.GetTempPath(), $"does-not-exist-{Guid.NewGuid():N}.mbtiles");
        var service = CreateService(TileSourceType.MbTilesOffline, missingPath);

        var layer = service.CreateBaseMapLayer();

        Assert.Equal("OpenStreetMap", layer.Name);
    }

    [Fact]
    public void CreateBaseMapLayer_MbTilesWithEmptyPath_FallsBackToOsm()
    {
        var service = CreateService(TileSourceType.MbTilesOffline, string.Empty);

        var layer = service.CreateBaseMapLayer();

        Assert.Equal("OpenStreetMap", layer.Name);
    }

    private static MapTileSourceService CreateService(TileSourceType tileSource, string mbTilesFilePath)
    {
        var configuration = Substitute.For<IConfigurationService>();
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
