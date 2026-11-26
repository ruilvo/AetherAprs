// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using BruTile;
using BruTile.Predefined;
using Mapsui.Projections;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace AetherAprs.Services.UI;

/// <summary>
/// A <see cref="ILocalTileSource"/> that serves tiles from a MapBox MBTiles package
/// (a SQLite database) without a network connection.
/// </summary>
public sealed class MbTilesTileSource : ILocalTileSource
{
    private const string DefaultFormat = "png";

    private readonly string _connectionString;

    /// <summary>
    /// Initializes a new instance from an MBTiles package file, reading the schema
    /// and metadata from the package's <c>metadata</c> table.
    /// </summary>
    /// <param name="filePath">Path to the <c>.mbtiles</c> file.</param>
    /// <exception cref="FileNotFoundException">The file does not exist.</exception>
    public MbTilesTileSource(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"The MBTiles file does not exist: '{filePath}'", filePath);
        }

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = filePath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false
        }.ToString();

        Dictionary<string, string> metadata = ReadMetadata(_connectionString);

        string format = GetString(metadata, "format") ?? DefaultFormat;
        string? name = GetString(metadata, "name");
        string? attribution = GetString(metadata, "attribution");
        int[]? zoomLevels = ReadZoomLevels(metadata);
        Extent extent = ReadExtent(metadata);

        Schema = new GlobalSphericalMercator(format, YAxis.TMS, zoomLevels, name, extent);
        Name = name ?? string.Empty;
        Attribution = new Attribution(attribution ?? string.Empty);
    }

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

        await using SqliteConnection connection = new(_connectionString);
        await connection.OpenAsync().ConfigureAwait(false);

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            "SELECT tile_data FROM tiles WHERE zoom_level = $zoom AND tile_column = $column AND tile_row = $row;";
        command.Parameters.AddWithValue("$zoom", index.Level);
        command.Parameters.AddWithValue("$column", index.Col);
        command.Parameters.AddWithValue("$row", index.Row);

        object? result = await command.ExecuteScalarAsync().ConfigureAwait(false);
        return result is byte[] data && data.Length > 0 ? data : null;
    }

    private static Dictionary<string, string> ReadMetadata(string connectionString)
    {
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

    private static string? GetString(IReadOnlyDictionary<string, string> metadata, string key) =>
        metadata.TryGetValue(key, out string? value) && !string.IsNullOrWhiteSpace(value) ? value : null;

    private static int[]? ReadZoomLevels(IReadOnlyDictionary<string, string> metadata)
    {
        if (!int.TryParse(GetString(metadata, "minzoom"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int min) ||
            !int.TryParse(GetString(metadata, "maxzoom"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int max) ||
            min > max)
        {
            return null;
        }

        return [.. Enumerable.Range(min, max - min + 1)];
    }

    private static Extent ReadExtent(IReadOnlyDictionary<string, string> metadata)
    {
        string? bounds = GetString(metadata, "bounds");
        if (bounds is null)
        {
            return default;
        }

        // MBTiles bounds are "minLon,minLat,maxLon,maxLat" in WGS84.
        string[] parts = bounds.Split(',');
        if (parts.Length != 4 ||
            !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double minLon) ||
            !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double minLat) ||
            !double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double maxLon) ||
            !double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out double maxLat))
        {
            return default;
        }

        (double minX, double minY) = SphericalMercator.FromLonLat(minLon, minLat);
        (double maxX, double maxY) = SphericalMercator.FromLonLat(maxLon, maxLat);
        return new Extent(minX, minY, maxX, maxY);
    }
}
