// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

using AetherAprs.Services.UI;
using AetherAprs.Tests.Helpers;

using BruTile;

using Xunit;

namespace AetherAprs.Tests.Services.UI;

public sealed class MbTilesTileSourceTests
{
    [Fact]
    public void Constructor_ReadsNameAndAttribution()
    {
        string directory = MbTilesTestFixture.CreateTemporaryDirectory();
        try
        {
            string path = MbTilesTestFixture.Create(directory);

            MbTilesTileSource source = new(path);

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
        string directory = MbTilesTestFixture.CreateTemporaryDirectory();
        try
        {
            string path = MbTilesTestFixture.Create(directory);

            MbTilesTileSource source = new(path);

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
        string directory = MbTilesTestFixture.CreateTemporaryDirectory();
        try
        {
            string path = MbTilesTestFixture.Create(directory);

            MbTilesTileSource source = new(path);

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
        string path = Path.Combine(Path.GetTempPath(), $"does-not-exist-{Guid.NewGuid():N}.mbtiles");

        Assert.Throws<FileNotFoundException>(() => new MbTilesTileSource(path));
    }

    [Fact]
    public async Task GetTileAsync_ExistingTile_ReturnsData()
    {
        string directory = MbTilesTestFixture.CreateTemporaryDirectory();
        try
        {
            string path = MbTilesTestFixture.Create(directory);
            MbTilesTileSource source = new(path);

            TileInfo tile = new() { Index = new TileIndex(0, 0, 0) };
            byte[]? data = await source.GetTileAsync(tile);

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
        string directory = MbTilesTestFixture.CreateTemporaryDirectory();
        try
        {
            string path = MbTilesTestFixture.Create(directory);
            MbTilesTileSource source = new(path);

            // No tile exists at column 5 / row 5 / zoom 0.
            TileInfo tile = new() { Index = new TileIndex(5, 5, 0) };
            byte[]? data = await source.GetTileAsync(tile);

            Assert.Null(data);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Constructor_GarbageFile_ThrowsCorruptPackage()
    {
        string directory = MbTilesTestFixture.CreateTemporaryDirectory();
        try
        {
            string path = MbTilesTestFixture.CreateGarbageFile(directory);

            CorruptTilePackageException ex =
                Assert.Throws<CorruptTilePackageException>(() => new MbTilesTileSource(path));

            Assert.Equal(path, ex.FilePath);
            Assert.Equal(TilePackageFault.Unreadable, ex.Fault);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Constructor_DatabaseWithoutMbTilesTables_ThrowsCorruptPackage()
    {
        string directory = MbTilesTestFixture.CreateTemporaryDirectory();
        try
        {
            string path = MbTilesTestFixture.CreateWithoutMbTilesSchema(directory);

            CorruptTilePackageException ex =
                Assert.Throws<CorruptTilePackageException>(() => new MbTilesTileSource(path));

            Assert.Equal(TilePackageFault.InvalidSchema, ex.Fault);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData("format")] // missing format
    [InlineData("bounds")] // missing bounds
    [InlineData("minzoom")] // missing minzoom
    [InlineData("maxzoom")] // missing maxzoom
    public void Constructor_MissingRequiredMetadata_ThrowsCorruptPackage(string missingKey)
    {
        string directory = MbTilesTestFixture.CreateTemporaryDirectory();
        try
        {
            string path = CreatePackageWithout(directory, missingKey);

            CorruptTilePackageException ex =
                Assert.Throws<CorruptTilePackageException>(() => new MbTilesTileSource(path));

            Assert.Equal(TilePackageFault.MissingMetadata, ex.Fault);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData("10,40,-10,50")] // min longitude is not less than max
    [InlineData("-10,50,10,40")] // min latitude is not less than max
    [InlineData("-200,40,10,50")] // longitude outside WGS84
    [InlineData("-10,-95,10,50")] // latitude outside the Web Mercator range
    [InlineData("-10,40,10")] // not four components
    [InlineData("-10,40,ten,50")] // not numeric
    [InlineData("NaN,40,10,50")] // not finite
    public void Constructor_MalformedBounds_ThrowsCorruptPackage(string bounds)
    {
        string directory = MbTilesTestFixture.CreateTemporaryDirectory();
        try
        {
            string path = CreatePackage(directory, "bounds", bounds);

            CorruptTilePackageException ex =
                Assert.Throws<CorruptTilePackageException>(() => new MbTilesTileSource(path));

            Assert.Equal(TilePackageFault.InvalidBounds, ex.Fault);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData("2", "1")] // minzoom greater than maxzoom
    [InlineData("-1", "2")] // negative minimum
    [InlineData("0", "31")] // maximum beyond the supported range
    [InlineData("zero", "2")] // not an integer
    public void Constructor_MalformedZoomLevels_ThrowsCorruptPackage(string minZoom, string maxZoom)
    {
        string directory = MbTilesTestFixture.CreateTemporaryDirectory();
        try
        {
            string path = CreatePackage(directory, "minzoom", minZoom, "maxzoom", maxZoom);

            CorruptTilePackageException ex =
                Assert.Throws<CorruptTilePackageException>(() => new MbTilesTileSource(path));

            Assert.Equal(TilePackageFault.InvalidMetadata, ex.Fault);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string CreatePackageWithout(string directory, string missingKey)
    {
        Dictionary<string, string> metadata = CreateMetadata();
        metadata.Remove(missingKey);
        return MbTilesTestFixture.CreateWithMetadata(directory, "missing-metadata.mbtiles", metadata);
    }

    private static string CreatePackage(string directory, params string[] overrides)
    {
        Dictionary<string, string> metadata = CreateMetadata();
        for (int i = 0; i < overrides.Length; i += 2)
        {
            metadata[overrides[i]] = overrides[i + 1];
        }

        return MbTilesTestFixture.CreateWithMetadata(directory, "invalid-metadata.mbtiles", metadata);
    }

    private static Dictionary<string, string> CreateMetadata() => new(StringComparer.Ordinal)
    {
        ["name"] = "Test Map",
        ["format"] = "png",
        ["bounds"] = "-10,40,10,50",
        ["minzoom"] = "0",
        ["maxzoom"] = "2",
        ["attribution"] = "Test attribution"
    };
}
