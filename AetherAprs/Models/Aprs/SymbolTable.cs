// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;

namespace AetherAprs.Models.Aprs;

/// <summary>
/// APRS symbol table identifier.
/// </summary>
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