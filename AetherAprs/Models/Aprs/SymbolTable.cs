// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AetherAprs.Models.Aprs;

/// <summary>
/// APRS symbol table identifier.
/// </summary>
[JsonConverter(typeof(SymbolTableJsonConverter))]
public enum SymbolTable : byte
{
    /// <summary>
    /// Primary symbol table ('/').
    /// </summary>
    Primary = 0,

    /// <summary>
    /// Alternate symbol table ('\').
    /// </summary>
    Alternate = 1,
}

/// <summary>
/// Converts APRS symbol tables to and from their single-character JSON representation.
/// </summary>
public sealed class SymbolTableJsonConverter : JsonConverter<SymbolTable>
{
    public override SymbolTable Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException("Symbol table must be '/' or '\\'.");
        }

        return reader.GetString() switch
        {
            "/" => SymbolTable.Primary,
            "\\" => SymbolTable.Alternate,
            _ => throw new JsonException("Symbol table must be '/' or '\\'.")
        };
    }

    public override void Write(
        Utf8JsonWriter writer,
        SymbolTable value,
        JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToChar().ToString());
    }
}

/// <summary>
/// Extension methods for <see cref="SymbolTable"/>.
/// </summary>
public static class SymbolTableExtensions
{
    /// <summary>
    /// Converts a <see cref="SymbolTable"/> to its APRS character representation.
    /// </summary>
    public static char ToChar(this SymbolTable table) => table switch
    {
        SymbolTable.Primary => '/',
        SymbolTable.Alternate => '\\',
        _ => throw new InvalidOperationException($"Invalid symbol table: {table}")
    };

    /// <summary>
    /// Converts a character to a <see cref="SymbolTable"/>.
    /// </summary>
    /// <param name="c">The character ('/' for primary, '\' for alternate).</param>
    /// <returns>The corresponding symbol table.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if the character is not '/' or '\'.</exception>
    public static SymbolTable ToSymbolTable(this char c) => c switch
    {
        '/' => SymbolTable.Primary,
        '\\' => SymbolTable.Alternate,
        _ => throw new ArgumentOutOfRangeException(nameof(c), $"Invalid symbol table character: '{c}'")
    };
}