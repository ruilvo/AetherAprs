// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

namespace AetherAprs.Models.Aprs;

/// <summary>
/// Represents an APRS symbol (table identifier + code character).
/// </summary>
public readonly record struct Symbol
{
    /// <summary>
    /// Gets the symbol table identifier character ('/' or '\').
    /// </summary>
    public char Table { get; }

    /// <summary>
    /// Gets the symbol code character.
    /// </summary>
    public char Code { get; }

    /// <summary>
    /// Initializes a new instance of <see cref="Symbol"/>.
    /// </summary>
    /// <param name="table">The symbol table ('/' for primary, '\' for alternate).</param>
    /// <param name="code">The symbol code character.</param>
    public Symbol(char table, char code)
    {
        Table = table;
        Code = code;
    }
}