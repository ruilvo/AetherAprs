// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using BruTile;
using BruTile.Predefined;

using Mapsui.Projections;

using Microsoft.Data.Sqlite;

namespace AetherAprs.Services.UI;

/// <summary>
/// A <see cref="ILocalTileSource"/> that serves tiles from a MapBox MBTiles package
/// (a SQLite database) without a network connection.
/// </summary>
public sealed class MbTilesTileSource : ILocalTileSource
{
    private const int MaximumZoomLevel = 30;

    // MBTiles bounds use WGS84; latitudes beyond the Web Mercator limit cannot be
    // projected, so a package declaring them is malformed.
    private const double MaximumMercatorLatitude = 85.0511287798066;

    private readonly string _connectionString;

    /// <summary>
    /// Initializes a new instance from an MBTiles package file, reading the schema
    /// and metadata from the package's <c>metadata</c> table.
    /// </summary>
    /// <param name="filePath">Path to the <c>.mbtiles</c> file.</param>
    /// <exception cref="FileNotFoundException">The file does not exist.</exception>
    /// <exception cref="CorruptTilePackageException">
    /// The file exists but is not a valid MBTiles package, or its required metadata
    /// (format, bounds, minzoom, maxzoom) is missing or malformed.
    /// </exception>
    public MbTilesTileSource(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"The MBTiles file does not exist: '{filePath}'", filePath);
        }

        FilePath = filePath;
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = filePath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false
        }.ToString();

        Dictionary<string, string> metadata = LoadMetadata(filePath, _connectionString);

        string format = RequireString(metadata, "format", filePath);
        string? name = GetString(metadata, "name");
        string? attribution = GetString(metadata, "attribution");
        int[] zoomLevels = ReadZoomLevels(metadata, filePath);
        Extent extent = ReadExtent(metadata, filePath);

        Schema = new GlobalSphericalMercator(format, YAxis.TMS, zoomLevels, name, extent);
        Name = name ?? string.Empty;
        Attribution = new Attribution(attribution ?? string.Empty);
    }

    /// <summary>The path of the package this source reads.</summary>
    public string FilePath { get; }

    /// <inheritdoc />
    public ITileSchema Schema { get; }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public Attribution Attribution { get; }

    /// <inheritdoc />
    public async Task<byte[]?> GetTileAsync(TileInfo tileInfo)
    {
        TileIndex index = tileInfo.Index;

        try
        {
            await using SqliteConnection connection = new(_connectionString);
            await connection.OpenAsync().ConfigureAwait(false);

            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText =
                "SELECT tile_data FROM tiles WHERE zoom_level = $zoom AND tile_column = $column AND tile_row = $row;";
            command.Parameters.AddWithValue("$zoom", index.Level);
            command.Parameters.AddWithValue("$column", index.Col);
            command.Parameters.AddWithValue("$row", index.Row);

            object? result = await command.ExecuteScalarAsync().ConfigureAwait(false);

            // A valid package simply has no row for a tile outside its coverage; that is
            // different from a damaged package and stays a null (missing) result.
            return result is byte[] data && data.Length > 0 ? data : null;
        }
        catch (SqliteException ex)
        {
            throw new CorruptTilePackageException(
                FilePath,
                TilePackageFault.Unreadable,
                $"The MBTiles package could not be read: '{FilePath}'",
                ex);
        }
    }

    private static Dictionary<string, string> LoadMetadata(string filePath, string connectionString)
    {
        try
        {
            EnsureRequiredTables(filePath, connectionString);

            using SqliteConnection connection = new(connectionString);
            connection.Open();

            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT name, value FROM metadata;";

            using SqliteDataReader reader = command.ExecuteReader();
            Dictionary<string, string> metadata = [with(StringComparer.OrdinalIgnoreCase)];
            while (reader.Read())
            {
                metadata[reader.GetString(0)] = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
            }

            return metadata;
        }
        catch (CorruptTilePackageException)
        {
            throw;
        }
        catch (Exception ex) when (ex is SqliteException or InvalidCastException)
        {
            // A non-SQLite file, or a file whose metadata values are not text, is corrupt
            // rather than absent.
            throw new CorruptTilePackageException(
                filePath,
                TilePackageFault.Unreadable,
                $"The MBTiles package could not be read: '{filePath}'",
                ex);
        }
    }

    private static void EnsureRequiredTables(string filePath, string connectionString)
    {
        using SqliteConnection connection = new(connectionString);
        connection.Open();

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name IN ('tiles', 'metadata');";

        object? result = command.ExecuteScalar();
        long tableCount = result is null ? 0L : Convert.ToInt64(result, CultureInfo.InvariantCulture);
        if (tableCount != 2)
        {
            throw new CorruptTilePackageException(
                filePath,
                TilePackageFault.InvalidSchema,
                $"The MBTiles package must contain a 'tiles' and a 'metadata' table: '{filePath}'");
        }
    }

    private static string? GetString(IReadOnlyDictionary<string, string> metadata, string key) =>
        metadata.TryGetValue(key, out string? value) && !string.IsNullOrWhiteSpace(value) ? value : null;

    private static string RequireString(IReadOnlyDictionary<string, string> metadata, string key, string filePath) =>
        GetString(metadata, key)
        ?? throw new CorruptTilePackageException(
            filePath,
            TilePackageFault.MissingMetadata,
            $"The MBTiles package is missing the required '{key}' metadata: '{filePath}'");

    private static int[] ReadZoomLevels(IReadOnlyDictionary<string, string> metadata, string filePath)
    {
        string minText = RequireString(metadata, "minzoom", filePath);
        string maxText = RequireString(metadata, "maxzoom", filePath);

        if (!int.TryParse(minText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int min) ||
            !int.TryParse(maxText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int max))
        {
            throw new CorruptTilePackageException(
                filePath,
                TilePackageFault.InvalidMetadata,
                $"The MBTiles zoom levels are not integers (minzoom '{minText}', maxzoom '{maxText}'): '{filePath}'");
        }

        if (min < 0 || max > MaximumZoomLevel || min > max)
        {
            throw new CorruptTilePackageException(
                filePath,
                TilePackageFault.InvalidMetadata,
                $"The MBTiles zoom range {min}..{max} is invalid; it must satisfy 0 <= minzoom <= maxzoom <= {MaximumZoomLevel}: '{filePath}'");
        }

        return [.. Enumerable.Range(min, max - min + 1)];
    }

    private static Extent ReadExtent(IReadOnlyDictionary<string, string> metadata, string filePath)
    {
        // MBTiles bounds are "minLon,minLat,maxLon,maxLat" in WGS84.
        string bounds = RequireString(metadata, "bounds", filePath);
        string[] parts = bounds.Split(',');

        if (parts.Length != 4 ||
            !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double minLon) ||
            !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double minLat) ||
            !double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double maxLon) ||
            !double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out double maxLat))
        {
            throw new CorruptTilePackageException(
                filePath,
                TilePackageFault.InvalidBounds,
                $"The MBTiles bounds '{bounds}' are not four comma-separated numbers: '{filePath}'");
        }

        if (minLon < -180.0 || maxLon > 180.0 ||
            minLat < -MaximumMercatorLatitude || maxLat > MaximumMercatorLatitude ||
            minLon >= maxLon || minLat >= maxLat ||
            !double.IsFinite(minLon) || !double.IsFinite(minLat) ||
            !double.IsFinite(maxLon) || !double.IsFinite(maxLat))
        {
            throw new CorruptTilePackageException(
                filePath,
                TilePackageFault.InvalidBounds,
                $"The MBTiles bounds '{bounds}' are not a sane WGS84 extent: '{filePath}'");
        }

        (double minX, double minY) = SphericalMercator.FromLonLat(minLon, minLat);
        (double maxX, double maxY) = SphericalMercator.FromLonLat(maxLon, maxLat);
        return new Extent(minX, minY, maxX, maxY);
    }
}
