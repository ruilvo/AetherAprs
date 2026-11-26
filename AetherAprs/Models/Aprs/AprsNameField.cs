// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;

namespace AetherAprs.Models.Aprs;

/// <summary>
/// The nine-character name field shared by APRS objects, items, bulletins and
/// addressees.
/// </summary>
/// <remarks>
/// These names are fixed-width on the air: an object name longer than nine characters
/// shifts the status byte and the position that follow it, so every receiver decodes a
/// different packet. Received names are already bounded by the field width; this type
/// guards the names this application writes.
/// </remarks>
public static class AprsNameField
{
    /// <summary>Width of the APRS name field.</summary>
    public const int MaximumLength = 9;

    /// <summary>True when <paramref name="name"/> fits the field.</summary>
    public static bool IsValid(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > MaximumLength)
        {
            return false;
        }

        foreach (char character in name)
        {
            if (char.IsControl(character))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Throws when <paramref name="name"/> does not fit the field.</summary>
    /// <exception cref="ArgumentException">The name is empty or wider than the field.</exception>
    public static void EnsureValid(string? name, string parameterName)
    {
        if (!IsValid(name))
        {
            throw new ArgumentException(
                $"APRS names must be 1-{MaximumLength} printable characters, got '{name}'.",
                parameterName);
        }
    }
}
