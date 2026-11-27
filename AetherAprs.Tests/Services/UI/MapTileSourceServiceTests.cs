// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Configuration;
using AetherAprs.Configuration.Settings;
using AetherAprs.Services.Configuration;
using AetherAprs.Services.UI;
using AetherAprs.Tests.Helpers;
using BruTile;
using Mapsui.Layers;
using Mapsui.Tiling.Layers;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using System;
using System.IO;
using System.Threading.Tasks;
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
        Assert.Equal(MapTileSourceFallbackReason.NotConfigured, service.Status.FallbackReason);
        Assert.True(service.Status.IsFallback);
    }

    [Fact]
    public void CreateBaseMapLayer_MbTilesWithMissingFile_ReportsFileMissing()
    {
        string missingPath = Path.Combine(Path.GetTempPath(), $"does-not-exist-{Guid.NewGuid():N}.mbtiles");
        MapTileSourceService service = CreateService(TileSourceType.MbTilesOffline, missingPath);

        service.CreateBaseMapLayer();

        Assert.Equal(TileSourceType.OsmOnline, service.Status.ActiveSource);
        Assert.Equal(MapTileSourceFallbackReason.FileMissing, service.Status.FallbackReason);
        Assert.Equal(missingPath, service.Status.Detail);
        Assert.True(service.Status.IsFallback);
    }

    [Fact]
    public void CreateBaseMapLayer_MbTilesWithCorruptFile_FallsBackToOsmAndReportsCorruption()
    {
        string directory = MbTilesTestFixture.CreateTemporaryDirectory();
        try
        {
            string path = MbTilesTestFixture.CreateGarbageFile(directory);
            int onlineLayerCalls = 0;
            MapTileSourceService service = CreateService(
                TileSourceType.MbTilesOffline,
                path,
                _ =>
                {
                    onlineLayerCalls++;
                    return Substitute.For<ILayer>();
                });

            ILayer layer = service.CreateBaseMapLayer();

            Assert.Equal(1, onlineLayerCalls);
            Assert.NotNull(layer);
            Assert.Equal(TileSourceType.OsmOnline, service.Status.ActiveSource);
            Assert.Equal(MapTileSourceFallbackReason.PackageCorrupt, service.Status.FallbackReason);
            Assert.True(service.Status.IsFallback);
            Assert.False(string.IsNullOrWhiteSpace(service.Status.Detail));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void CreateBaseMapLayer_MbTilesWithValidFile_ReportsOfflineWithoutFallback()
    {
        string directory = MbTilesTestFixture.CreateTemporaryDirectory();
        try
        {
            string path = MbTilesTestFixture.Create(directory);
            MapTileSourceService service = CreateService(TileSourceType.MbTilesOffline, path);

            service.CreateBaseMapLayer();

            Assert.Equal(TileSourceType.MbTilesOffline, service.Status.ActiveSource);
            Assert.Equal(MapTileSourceFallbackReason.None, service.Status.FallbackReason);
            Assert.False(service.Status.IsFallback);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void CreateBaseMapLayer_OsmOnline_ReportsOnlineWithoutFallback()
    {
        MapTileSourceService service = CreateService(TileSourceType.OsmOnline, string.Empty);

        service.CreateBaseMapLayer();

        Assert.Equal(TileSourceType.OsmOnline, service.Status.ActiveSource);
        Assert.Equal(MapTileSourceFallbackReason.None, service.Status.FallbackReason);
    }

    [Fact]
    public void CreateBaseMapLayer_MbTilesWithValidFile_ExposesAttributionFromMetadata()
    {
        string directory = MbTilesTestFixture.CreateTemporaryDirectory();
        try
        {
            string path = MbTilesTestFixture.Create(directory);
            MapTileSourceService service = CreateService(TileSourceType.MbTilesOffline, path);

            TileLayer layer = Assert.IsType<TileLayer>(service.CreateBaseMapLayer());
            MbTilesTileSource source = Assert.IsType<MbTilesTileSource>(layer.TileSource);

            Assert.Equal("Test attribution", source.Attribution.Text);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task CreateBaseMapLayer_OfflineFixture_ServesTileWithoutCreatingOnlineLayer()
    {
        string directory = MbTilesTestFixture.CreateTemporaryDirectory();
        try
        {
            string path = MbTilesTestFixture.Create(directory);
            int onlineLayerCalls = 0;

            // The online factory is the only seam that can reach the network. It must not
            // be touched while the package is usable, so reading a tile issues no requests.
            MapTileSourceService service = CreateService(
                TileSourceType.MbTilesOffline,
                path,
                _ =>
                {
                    onlineLayerCalls++;
                    return Substitute.For<ILayer>();
                });

            TileLayer layer = Assert.IsType<TileLayer>(service.CreateBaseMapLayer());
            MbTilesTileSource source = Assert.IsType<MbTilesTileSource>(layer.TileSource);

            byte[]? data = await source.GetTileAsync(new TileInfo { Index = new TileIndex(0, 0, 0) });

            Assert.Equal([1, 2, 3, 4], data);
            Assert.Equal(0, onlineLayerCalls);
            Assert.Equal(MapTileSourceFallbackReason.None, service.Status.FallbackReason);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static MapTileSourceService CreateService(
        TileSourceType tileSource,
        string mbTilesFilePath,
        Func<string, ILayer>? onlineLayerFactory = null)
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

        return onlineLayerFactory is null
            ? new MapTileSourceService(configuration, NullLogger<MapTileSourceService>.Instance)
            : new MapTileSourceService(configuration, NullLogger<MapTileSourceService>.Instance, onlineLayerFactory);
    }
}
