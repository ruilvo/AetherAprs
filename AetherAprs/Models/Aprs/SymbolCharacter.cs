// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;

namespace AetherAprs.Models.Aprs;

/// <summary>
/// Represents a single APRS symbol character with validation.
/// APRS symbols consist of a table character ('/' or '\') and a code character (printable ASCII 33-126).
/// </summary>
public readonly struct SymbolCharacter : IEquatable<SymbolCharacter>
{
    private readonly char _value;

    private SymbolCharacter(char value)
    {
        _value = value;
    }

    /// <summary>
    /// Gets the character value.
    /// </summary>
    public char Value => _value;

    /// <summary>
    /// Tries to create a SymbolCharacter from a string.
    /// </summary>
    /// <param name="value">String containing exactly one character.</param>
    /// <param name="isTableCharacter">True if validating a table character (only '/' or '\'), false for code character.</param>
    /// <param name="result">The created SymbolCharacter if successful.</param>
    /// <returns>True if the value is valid, false otherwise.</returns>
    public static bool TryCreate(string? value, bool isTableCharacter, out SymbolCharacter result)
    {
        result = default;

        if (string.IsNullOrEmpty(value) || value.Length != 1)
        {
            return false;
        }

        var ch = value[0];

        if (isTableCharacter)
        {
            // Table character must be '/' or '\'
            if (ch != '/' && ch != '\\')
            {
                return false;
            }
        }
        else
        {
            // Code character must be printable ASCII (33-126)
            if (ch < 33 || ch > 126)
            {
                return false;
            }
        }

        result = new SymbolCharacter(ch);
        return true;
    }

    /// <summary>
    /// Creates a SymbolCharacter from a validated character.
    /// </summary>
    /// <param name="value">The character value.</param>
    /// <returns>A SymbolCharacter instance.</returns>
    /// <remarks>
    /// This method assumes the character has already been validated. Use TryCreate for user input.
    /// </remarks>
    public static SymbolCharacter FromChar(char value) => new(value);

    /// <summary>
    /// Implicitly converts a SymbolCharacter to a string.
    /// </summary>
    public static implicit operator string(SymbolCharacter symbol) => symbol._value.ToString();

    /// <summary>
    /// Implicitly converts a SymbolCharacter to a char.
    /// </summary>
    public static implicit operator char(SymbolCharacter symbol) => symbol._value;

    /// <inheritdoc/>
    public override string ToString() => _value.ToString();

    /// <inheritdoc/>
    public bool Equals(SymbolCharacter other) => _value == other._value;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is SymbolCharacter other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => _value.GetHashCode();

    /// <summary>
    /// Equality operator.
    /// </summary>
    public static bool operator ==(SymbolCharacter left, SymbolCharacter right) => left.Equals(right);

    /// <summary>
    /// Inequality operator.
    /// </summary>
    public static bool operator !=(SymbolCharacter left, SymbolCharacter right) => !left.Equals(right);
}
