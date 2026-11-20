// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Services.UI;
using AetherAprs.Tests.Helpers;
using BruTile;
using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace AetherAprs.Tests.Services.UI;

public sealed class MbTilesTileSourceTests
{
    [Fact]
    public void Constructor_ReadsNameAndAttribution()
    {
        var directory = MbTilesTestFixture.CreateTemporaryDirectory();
        try
        {
            var path = MbTilesTestFixture.Create(directory);

            var source = new MbTilesTileSource(path);

            Assert.Equal("Test Map", source.Name);
            Assert.Equal("Test attribution", source.Attribution.Text);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Constructor_ReadsSchemaFormatAndZoomLevels()
    {
        var directory = MbTilesTestFixture.CreateTemporaryDirectory();
        try
        {
            var path = MbTilesTestFixture.Create(directory);

            var source = new MbTilesTileSource(path);

            Assert.Equal("png", source.Schema.Format);
            Assert.Equal(YAxis.TMS, source.Schema.YAxis);
            Assert.Equal(3, source.Schema.Resolutions.Count);
            Assert.Contains(0, source.Schema.Resolutions.Keys);
            Assert.Contains(2, source.Schema.Resolutions.Keys);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Constructor_ReadsExtentFromBounds()
    {
        var directory = MbTilesTestFixture.CreateTemporaryDirectory();
        try
        {
            var path = MbTilesTestFixture.Create(directory);

            var source = new MbTilesTileSource(path);

            // Bounds -10,40,10,50 (WGS84) converted to Spherical Mercator meters.
            Assert.True(source.Schema.Extent.MinX < source.Schema.Extent.MaxX);
            Assert.True(source.Schema.Extent.MinY < source.Schema.Extent.MaxY);
            Assert.True(source.Schema.Extent.Width > 0);
            Assert.True(source.Schema.Extent.Height > 0);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Constructor_MissingFile_ThrowsFileNotFound()
    {
        var path = Path.Combine(Path.GetTempPath(), $"does-not-exist-{Guid.NewGuid():N}.mbtiles");

        Assert.Throws<FileNotFoundException>(() => new MbTilesTileSource(path));
    }

    [Fact]
    public async Task GetTileAsync_ExistingTile_ReturnsData()
    {
        var directory = MbTilesTestFixture.CreateTemporaryDirectory();
        try
        {
            var path = MbTilesTestFixture.Create(directory);
            var source = new MbTilesTileSource(path);

            var tile = new TileInfo { Index = new TileIndex(0, 0, 0) };
            var data = await source.GetTileAsync(tile);

            Assert.NotNull(data);
            Assert.Equal([1, 2, 3, 4], data);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task GetTileAsync_MissingTile_ReturnsNull()
    {
        var directory = MbTilesTestFixture.CreateTemporaryDirectory();
        try
        {
            var path = MbTilesTestFixture.Create(directory);
            var source = new MbTilesTileSource(path);

            // No tile exists at column 5 / row 5 / zoom 0.
            var tile = new TileInfo { Index = new TileIndex(5, 5, 0) };
            var data = await source.GetTileAsync(tile);

            Assert.Null(data);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
