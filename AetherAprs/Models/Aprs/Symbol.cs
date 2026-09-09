// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

namespace AetherAprs.Models.Aprs;

/// <summary>
/// Represents an APRS symbol (table identifier + code character + optional overlay).
/// </summary>
public readonly record struct Symbol
{
    /// <summary>
    /// Gets the symbol table (primary or alternate).
    /// </summary>
    public SymbolTable Table { get; }

    /// <summary>
    /// Gets the symbol code character.
    /// </summary>
    public SymbolCode Code { get; }

    /// <summary>
    /// Gets the optional overlay character (typically '0'-'9' or 'A'-'Z').
    /// When non-null, the overlay character modifies the symbol appearance.
    /// </summary>
    public char? Overlay { get; }

    /// <summary>
    /// Initializes a new instance of <see cref="Symbol"/>.
    /// </summary>
    /// <param name="table">The symbol table.</param>
    /// <param name="code">The symbol code.</param>
    /// <param name="overlay">Optional overlay character (typically '0'-'9' or 'A'-'Z').</param>
    public Symbol(SymbolTable table, SymbolCode code, char? overlay = null)
    {
        Table = table;
        Code = code;
        Overlay = overlay;
    }

    /// <summary>
    /// Gets the APRS symbol table character ('/' or '\').
    /// </summary>
    public char TableChar => Table.ToChar();

    /// <summary>
    /// Gets the APRS symbol code character.
    /// </summary>
    public char CodeChar => Code.ToChar();
}