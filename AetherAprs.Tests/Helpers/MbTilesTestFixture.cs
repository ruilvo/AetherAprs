// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using Microsoft.Data.Sqlite;
using System;
using System.IO;

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
        var directory = Path.Combine(Path.GetTempPath(), $"AetherAprsTests-{Guid.NewGuid():N}");
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
        var path = Path.Combine(directory, fileName);

        using var connection = new SqliteConnection($"Data Source={path};Pooling=false");
        connection.Open();

        Execute(connection, "CREATE TABLE metadata (name TEXT, value TEXT);");
        Execute(connection,
            "CREATE TABLE tiles (zoom_level INTEGER, tile_column INTEGER, tile_row INTEGER, tile_data BLOB);");

        InsertMetadata(connection, "name", "Test Map");
        InsertMetadata(connection, "format", "png");
        InsertMetadata(connection, "bounds", "-10,40,10,50");
        InsertMetadata(connection, "minzoom", "0");
        InsertMetadata(connection, "maxzoom", "2");
        InsertMetadata(connection, "attribution", "Test attribution");

        InsertTile(connection, 0, 0, 0, [1, 2, 3, 4]);

        return path;
    }

    private static void InsertMetadata(SqliteConnection connection, string name, string value)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO metadata (name, value) VALUES ($name, $value);";
        command.Parameters.AddWithValue("$name", name);
        command.Parameters.AddWithValue("$value", value);
        command.ExecuteNonQuery();
    }

    private static void InsertTile(SqliteConnection connection, int zoom, int column, int row, byte[] data)
    {
        using var command = connection.CreateCommand();
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
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
