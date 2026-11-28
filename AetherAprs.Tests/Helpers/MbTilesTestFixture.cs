// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.IO;

using Microsoft.Data.Sqlite;

namespace AetherAprs.Tests.Helpers;

/// <summary>
/// Builds minimal, self-contained MBTiles packages for tests, avoiding checked-in
/// binary fixtures. The package contains a <c>metadata</c> table and a single tile.
/// </summary>
internal static class MbTilesTestFixture
{
    /// <summary>
    /// Creates a temporary directory for a test and returns its path.
    /// </summary>
    public static string CreateTemporaryDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"AetherAprsTests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }

    /// <summary>
    /// Creates an MBTiles package inside <paramref name="directory"/> and returns its path.
    /// The package declares bounds <c>-10,40,10,50</c>, zoom levels 0–2, and a single
    /// tile at (0, 0, 0) whose data is <c>[1, 2, 3, 4]</c>.
    /// </summary>
    public static string Create(string directory, string fileName = "fixture.mbtiles")
    {
        Dictionary<string, string> metadata = new(StringComparer.Ordinal)
        {
            ["name"] = "Test Map",
            ["format"] = "png",
            ["bounds"] = "-10,40,10,50",
            ["minzoom"] = "0",
            ["maxzoom"] = "2",
            ["attribution"] = "Test attribution"
        };

        string path = CreateWithMetadata(directory, fileName, metadata);

        using SqliteConnection connection = new($"Data Source={path};Pooling=false");
        connection.Open();
        InsertTile(connection, 0, 0, 0, [1, 2, 3, 4]);

        return path;
    }

    /// <summary>
    /// Creates an MBTiles package with exactly the supplied metadata and no tiles.
    /// </summary>
    public static string CreateWithMetadata(
        string directory,
        string fileName,
        IReadOnlyDictionary<string, string> metadata)
    {
        string path = Path.Combine(directory, fileName);

        using SqliteConnection connection = new($"Data Source={path};Pooling=false");
        connection.Open();

        Execute(connection, "CREATE TABLE metadata (name TEXT, value TEXT);");
        Execute(connection,
            "CREATE TABLE tiles (zoom_level INTEGER, tile_column INTEGER, tile_row INTEGER, tile_data BLOB);");

        foreach ((string name, string value) in metadata)
        {
            InsertMetadata(connection, name, value);
        }

        return path;
    }

    /// <summary>
    /// Creates a SQLite database that is not an MBTiles package: it has no
    /// <c>metadata</c> or <c>tiles</c> table.
    /// </summary>
    public static string CreateWithoutMbTilesSchema(string directory, string fileName = "not-mbtiles.mbtiles")
    {
        string path = Path.Combine(directory, fileName);

        using SqliteConnection connection = new($"Data Source={path};Pooling=false");
        connection.Open();
        Execute(connection, "CREATE TABLE unrelated (id INTEGER PRIMARY KEY);");

        return path;
    }

    /// <summary>
    /// Writes arbitrary bytes to a <c>.mbtiles</c> path so it cannot be opened as SQLite.
    /// </summary>
    public static string CreateGarbageFile(string directory, string fileName = "garbage.mbtiles")
    {
        string path = Path.Combine(directory, fileName);
        File.WriteAllBytes(path, [0x00, 0xFF, 0x13, 0x37, 0x42, 0x99]);
        return path;
    }

    private static void InsertMetadata(SqliteConnection connection, string name, string value)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "INSERT INTO metadata (name, value) VALUES ($name, $value);";
        command.Parameters.AddWithValue("$name", name);
        command.Parameters.AddWithValue("$value", value);
        command.ExecuteNonQuery();
    }

    private static void InsertTile(SqliteConnection connection, int zoom, int column, int row, byte[] data)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            "INSERT INTO tiles (zoom_level, tile_column, tile_row, tile_data) VALUES ($zoom, $column, $row, $data);";
        command.Parameters.AddWithValue("$zoom", zoom);
        command.Parameters.AddWithValue("$column", column);
        command.Parameters.AddWithValue("$row", row);
        command.Parameters.AddWithValue("$data", data);
        command.ExecuteNonQuery();
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
