// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using Microsoft.Data.Sqlite;
using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AetherAprs.Services.UI;

/// <summary>
/// Downloads OpenStreetMap XYZ tiles and stores them in an MBTiles package, converting
/// the OSM row scheme (origin at the north edge) to the TMS scheme used by MBTiles
/// (origin at the south edge).
/// </summary>
public sealed class OfflineTilePackageDownloader : IOfflineTilePackageDownloader, IDisposable
{
    // OSM requires an identifiable User-Agent; generic defaults get blocked.
    private const string OsmUserAgent =
        "AetherAprs/1.0 (+https://github.com/ruilvo/AetherAprs)";
    private const string TileUrlTemplate = "https://tile.openstreetmap.org/{0}/{1}/{2}.png";
    private const int MaxZoomLevel = 19;

    private static readonly CompositeFormat TileUrlFormat = CompositeFormat.Parse(TileUrlTemplate);

    private readonly HttpClient _httpClient;

    public OfflineTilePackageDownloader()
        : this(new HttpClientHandler())
    {
    }

    internal OfflineTilePackageDownloader(HttpMessageHandler handler)
    {
        _httpClient = new HttpClient(handler);
        _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", OsmUserAgent);
    }

    /// <summary>
    /// Disposes the underlying <see cref="HttpClient"/>.
    /// </summary>
    public void Dispose() => _httpClient.Dispose();

    /// <inheritdoc />
    public async Task DownloadRegionAsync(
        OfflineDownloadRegion region,
        string outputPath,
        IProgress<OfflineDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(region);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        ValidateRegion(region);

        TryDeleteFile(outputPath);

        string? directory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        int totalTiles = CountTiles(region);
        int completed = 0;

        try
        {
            await using SqliteConnection connection = await OpenPackageAsync(region, outputPath, cancellationToken).ConfigureAwait(false);
            for (int zoom = region.MinZoom; zoom <= region.MaxZoom; zoom++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                TileRange range = GetTileRange(region, zoom);

                for (int x = range.MinX; x <= range.MaxX; x++)
                {
                    for (int y = range.MinY; y <= range.MaxY; y++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        byte[]? data = await DownloadTileAsync(zoom, x, y, cancellationToken).ConfigureAwait(false);
                        if (data is not null)
                        {
                            int tmsRow = (1 << zoom) - 1 - y;
                            await InsertTileAsync(connection, zoom, x, tmsRow, data, cancellationToken).ConfigureAwait(false);
                        }

                        completed++;
                        progress?.Report(new OfflineDownloadProgress(completed, totalTiles));
                    }
                }
            }
        }
        catch
        {
            TryDeleteFile(outputPath);
            throw;
        }
    }

    private static void ValidateRegion(OfflineDownloadRegion region)
    {
        if (region.MinLongitude >= region.MaxLongitude)
        {
            throw new ArgumentException("MinLongitude must be less than MaxLongitude.", nameof(region));
        }

        if (region.MinLatitude >= region.MaxLatitude)
        {
            throw new ArgumentException("MinLatitude must be less than MaxLatitude.", nameof(region));
        }

        if (region.MinZoom < 0 || region.MinZoom > region.MaxZoom || region.MaxZoom > MaxZoomLevel)
        {
            throw new ArgumentException(
                $"Zoom levels must satisfy 0 <= MinZoom <= MaxZoom <= {MaxZoomLevel}.",
                nameof(region));
        }
    }

    private async Task<byte[]?> DownloadTileAsync(int zoom, int x, int y, CancellationToken cancellationToken)
    {
        string url = string.Format(CultureInfo.InvariantCulture, TileUrlFormat, zoom, x, y);

        using HttpResponseMessage response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseContentRead, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            // Tile outside OSM coverage — skip rather than fail the whole download.
            return null;
        }

        response.EnsureSuccessStatusCode();

        byte[] data = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        return data.Length == 0 ? null : data;
    }

    private static async Task<SqliteConnection> OpenPackageAsync(
        OfflineDownloadRegion region,
        string outputPath,
        CancellationToken cancellationToken)
    {
        string connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = outputPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false
        }.ToString();

        SqliteConnection connection = new(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await ExecuteAsync(connection, "CREATE TABLE metadata (name TEXT, value TEXT);", cancellationToken).ConfigureAwait(false);
        await ExecuteAsync(connection,
            "CREATE TABLE tiles (zoom_level INTEGER, tile_column INTEGER, tile_row INTEGER, tile_data BLOB);",
            cancellationToken).ConfigureAwait(false);

        await InsertMetadataAsync(connection, "name", "Offline Map", cancellationToken).ConfigureAwait(false);
        await InsertMetadataAsync(connection, "format", "png", cancellationToken).ConfigureAwait(false);
        await InsertMetadataAsync(connection, "bounds", FormatBounds(region), cancellationToken).ConfigureAwait(false);
        await InsertMetadataAsync(connection, "minzoom", region.MinZoom.ToString(CultureInfo.InvariantCulture), cancellationToken).ConfigureAwait(false);
        await InsertMetadataAsync(connection, "maxzoom", region.MaxZoom.ToString(CultureInfo.InvariantCulture), cancellationToken).ConfigureAwait(false);
        await InsertMetadataAsync(connection, "attribution", "\u00A9 OpenStreetMap contributors", cancellationToken).ConfigureAwait(false);

        return connection;
    }

    private static string FormatBounds(OfflineDownloadRegion region) =>
        FormattableString.Invariant(
            $"{region.MinLongitude},{region.MinLatitude},{region.MaxLongitude},{region.MaxLatitude}");

    private static int CountTiles(OfflineDownloadRegion region)
    {
        int total = 0;
        for (int zoom = region.MinZoom; zoom <= region.MaxZoom; zoom++)
        {
            TileRange range = GetTileRange(region, zoom);
            total += (range.MaxX - range.MinX + 1) * (range.MaxY - range.MinY + 1);
        }

        return total;
    }

    private static TileRange GetTileRange(OfflineDownloadRegion region, int zoom)
    {
        int n = 1 << zoom;
        int minX = Math.Clamp(LongitudeToTileX(region.MinLongitude, zoom), 0, n - 1);
        int maxX = Math.Clamp(LongitudeToTileX(region.MaxLongitude, zoom), 0, n - 1);
        // Latitude decreases as tile Y increases: the northern bound yields the smaller row.
        int minY = Math.Clamp(LatitudeToTileY(region.MaxLatitude, zoom), 0, n - 1);
        int maxY = Math.Clamp(LatitudeToTileY(region.MinLatitude, zoom), 0, n - 1);
        return new TileRange(minX, minY, maxX, maxY);
    }

    private static int LongitudeToTileX(double longitude, int zoom)
    {
        int n = 1 << zoom;
        return (int)Math.Floor((longitude + 180.0) / 360.0 * n);
    }

    private static int LatitudeToTileY(double latitude, int zoom)
    {
        int n = 1 << zoom;
        double latRad = latitude * Math.PI / 180.0;
        return (int)Math.Floor((1.0 - Math.Log(Math.Tan(latRad) + 1.0 / Math.Cos(latRad)) / Math.PI) / 2.0 * n);
    }

    private static async Task InsertTileAsync(
        SqliteConnection connection,
        int zoom,
        int column,
        int row,
        byte[] data,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            "INSERT INTO tiles (zoom_level, tile_column, tile_row, tile_data) VALUES ($zoom, $column, $row, $data);";
        command.Parameters.AddWithValue("$zoom", zoom);
        command.Parameters.AddWithValue("$column", column);
        command.Parameters.AddWithValue("$row", row);
        command.Parameters.AddWithValue("$data", data);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task InsertMetadataAsync(
        SqliteConnection connection,
        string name,
        string value,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "INSERT INTO metadata (name, value) VALUES ($name, $value);";
        command.Parameters.AddWithValue("$name", name);
        command.Parameters.AddWithValue("$value", value);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task ExecuteAsync(
        SqliteConnection connection,
        string sql,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best-effort cleanup; a locked or read-only file is not fatal.
        }
    }

    private readonly record struct TileRange(int MinX, int MinY, int MaxX, int MaxY);
}
