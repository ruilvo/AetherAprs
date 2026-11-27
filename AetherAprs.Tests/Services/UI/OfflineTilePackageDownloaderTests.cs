// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Services.UI;
using AetherAprs.Tests.Helpers;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace AetherAprs.Tests.Services.UI;

public sealed class OfflineTilePackageDownloaderTests
{
    [Fact]
    public async Task DownloadRegion_SingleTile_WritesPackageAndMetadata()
    {
        string directory = MbTilesTestFixture.CreateTemporaryDirectory();
        try
        {
            string outputPath = Path.Combine(directory, "download.mbtiles");
            StubTileHandler handler = new(_ => Ok([1, 2, 3]));
            OfflineTilePackageDownloader downloader = new(handler);

            await downloader.DownloadRegionAsync(
                new OfflineDownloadRegion(-180, -85, 180, 85, 0, 0), outputPath,
                cancellationToken: TestContext.Current.CancellationToken);

            Dictionary<string, string> metadata = ReadMetadata(outputPath);
            Assert.Equal("Offline Map", metadata["name"]);
            Assert.Equal("png", metadata["format"]);
            Assert.Equal("-180,-85,180,85", metadata["bounds"]);
            Assert.Equal("0", metadata["minzoom"]);
            Assert.Equal("0", metadata["maxzoom"]);
            Assert.Equal("© OpenStreetMap contributors", metadata["attribution"]);

            List<StoredTile> tiles = ReadTiles(outputPath);
            StoredTile tile = Assert.Single(tiles);
            Assert.Equal((0, 0, 0), tile.Coordinates);
            Assert.Equal(new byte[] { 1, 2, 3 }, tile.Data);

            // The package is published from a temporary file only after a complete write.
            Assert.Empty(Directory.GetFiles(directory, "*.partial"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task DownloadRegion_InvertsRowToTmsScheme()
    {
        string directory = MbTilesTestFixture.CreateTemporaryDirectory();
        try
        {
            string outputPath = Path.Combine(directory, "download.mbtiles");
            StubTileHandler handler = new(_ => Ok([7]));
            OfflineTilePackageDownloader downloader = new(handler);

            // Northern hemisphere at zoom 1 resolves to XYZ row 0, which must be
            // stored under TMS row (2^1 - 1) - 0 = 1.
            await downloader.DownloadRegionAsync(
                new OfflineDownloadRegion(-180, 1, 180, 85, 1, 1), outputPath,
                cancellationToken: TestContext.Current.CancellationToken);

            List<StoredTile> tiles = ReadTiles(outputPath);
            Assert.Equal(2, tiles.Count);
            Assert.All(tiles, t => Assert.Equal(1, t.Coordinates.Row));
            Assert.Contains(tiles, t => t.Coordinates.Column == 0);
            Assert.Contains(tiles, t => t.Coordinates.Column == 1);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task DownloadRegion_NotFoundTile_IsSkipped()
    {
        string directory = MbTilesTestFixture.CreateTemporaryDirectory();
        try
        {
            string outputPath = Path.Combine(directory, "download.mbtiles");
            StubTileHandler handler = new(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
            OfflineTilePackageDownloader downloader = new(handler);

            await downloader.DownloadRegionAsync(
                new OfflineDownloadRegion(-180, -85, 180, 85, 0, 0), outputPath,
                cancellationToken: TestContext.Current.CancellationToken);

            // The package is still created but contains no tiles.
            Assert.Empty(ReadTiles(outputPath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task DownloadRegion_ReportsProgressToCompletion()
    {
        string directory = MbTilesTestFixture.CreateTemporaryDirectory();
        try
        {
            string outputPath = Path.Combine(directory, "download.mbtiles");
            StubTileHandler handler = new(_ => Ok([9]));
            OfflineTilePackageDownloader downloader = new(handler);
            ProgressCapture progress = new();

            await downloader.DownloadRegionAsync(
                new OfflineDownloadRegion(-180, 1, 180, 85, 1, 1), outputPath, progress,
                cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(2, progress.Reports.Count);
            Assert.Equal(2, progress.Reports[^1].TotalTiles);
            Assert.Equal(2, progress.Reports[^1].CompletedTiles);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData(20, 0, 10, 10, 0, 0)] // MinLongitude >= MaxLongitude
    [InlineData(-10, 20, 20, 10, 0, 0)] // MinLatitude >= MaxLatitude
    [InlineData(-10, 0, 20, 10, 2, 1)] // MinZoom > MaxZoom
    [InlineData(-10, 0, 20, 10, 0, 20)] // MaxZoom out of range
    public async Task DownloadRegion_InvalidRegion_Throws(
        double minLon, double minLat, double maxLon, double maxLat, int minZoom, int maxZoom)
    {
        OfflineTilePackageDownloader downloader = new(new StubTileHandler(_ => Ok([])));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            downloader.DownloadRegionAsync(
                new OfflineDownloadRegion(minLon, minLat, maxLon, maxLat, minZoom, maxZoom),
                Path.Combine(Path.GetTempPath(), $"invalid-{Guid.NewGuid():N}.mbtiles"),
                cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DownloadRegion_TooLittleFreeSpace_ThrowsBeforeContactingNetwork()
    {
        string directory = MbTilesTestFixture.CreateTemporaryDirectory();
        try
        {
            string outputPath = Path.Combine(directory, "quota.mbtiles");
            StubTileHandler handler = new(_ => Ok([1]));
            FakeFreeSpaceProvider freeSpace = new(OfflineTilePackageDownloader.PackageOverheadBytes);
            using OfflineTilePackageDownloader downloader = new(handler, freeSpace);

            InsufficientStorageException ex = await Assert.ThrowsAsync<InsufficientStorageException>(() =>
                downloader.DownloadRegionAsync(
                    new OfflineDownloadRegion(-180, -85, 180, 85, 0, 0), outputPath,
                    cancellationToken: TestContext.Current.CancellationToken));

            Assert.Equal(1, freeSpace.CallCount);
            Assert.Equal(0, handler.RequestCount);
            Assert.Equal(freeSpace.AvailableBytes, ex.AvailableBytes);
            Assert.True(ex.RequiredBytes > ex.AvailableBytes);
            Assert.False(File.Exists(outputPath));
            Assert.Empty(Directory.GetFiles(directory, "*.partial"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task DownloadRegion_Cancelled_LeavesNoOutputOrTemporaryPackage()
    {
        string directory = MbTilesTestFixture.CreateTemporaryDirectory();
        try
        {
            string outputPath = Path.Combine(directory, "cancelled.mbtiles");
            using CancellationTokenSource cancellation = new();
            StubTileHandler handler = new(_ =>
            {
                cancellation.Cancel();
                return Ok([1]);
            });
            using OfflineTilePackageDownloader downloader = new(handler);

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                downloader.DownloadRegionAsync(
                    new OfflineDownloadRegion(-180, 1, 180, 85, 1, 1), outputPath,
                    cancellationToken: cancellation.Token));

            // Neither the published package nor any temporary/journal artifact survives.
            Assert.Empty(Directory.GetFiles(directory, "cancelled.mbtiles*"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static HttpResponseMessage Ok(byte[] data) =>
        new(HttpStatusCode.OK) { Content = new ByteArrayContent(data) };

    private static Dictionary<string, string> ReadMetadata(string path)
    {
        using SqliteConnection connection = new($"Data Source={path};Pooling=false");
        connection.Open();

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT name, value FROM metadata;";

        using SqliteDataReader reader = command.ExecuteReader();
        Dictionary<string, string> metadata = [];
        while (reader.Read())
        {
            metadata[reader.GetString(0)] = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
        }

        return metadata;
    }

    private static List<StoredTile> ReadTiles(string path)
    {
        using SqliteConnection connection = new($"Data Source={path};Pooling=false");
        connection.Open();

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT zoom_level, tile_column, tile_row, tile_data FROM tiles ORDER BY tile_column;";

        using SqliteDataReader reader = command.ExecuteReader();
        List<StoredTile> tiles = [];
        while (reader.Read())
        {
            tiles.Add(new StoredTile(
                reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2),
                (byte[])reader[3]));
        }

        return tiles;
    }

    private sealed record StoredTile(int Zoom, int Column, int Row, byte[] Data)
    {
        public (int Zoom, int Column, int Row) Coordinates => (Zoom, Column, Row);
    }

    private sealed class ProgressCapture : IProgress<OfflineDownloadProgress>
    {
        public List<OfflineDownloadProgress> Reports { get; } = [];

        public void Report(OfflineDownloadProgress value) => Reports.Add(value);
    }

    private sealed class StubTileHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(responder(request));
        }
    }

    private sealed class FakeFreeSpaceProvider(long availableBytes) : IFreeSpaceProvider
    {
        public long AvailableBytes { get; } = availableBytes;

        public int CallCount { get; private set; }

        public long GetAvailableBytes(string directoryPath)
        {
            CallCount++;
            return AvailableBytes;
        }
    }
}
