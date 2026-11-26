// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using AetherAprs.Models.Aprs;

namespace AetherAprs.Configuration.Settings;

/// <summary>
/// Field-level validation shared by the settings document the configuration
/// file binds into and the immutable snapshot the application reads.
/// </summary>
/// <remarks>
/// Both graphs enforce the same rules because both can receive unvalidated
/// input: the document receives raw configuration values, and a snapshot can be
/// produced by a caller that has not validated its input. Keeping the rules
/// here means the two can never drift apart.
/// </remarks>
internal static class SettingsValidation
{
    /// <summary>
    /// Maximum allowed length of an APRS callsign, excluding the SSID.
    /// </summary>
    public const int MaxCallsignLength = 6;

    /// <summary>
    /// Validates and normalizes an APRS callsign.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The value is blank, longer than <see cref="MaxCallsignLength"/>, or
    /// contains a character that is not a letter or digit.
    /// </exception>
    public static string Callsign(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Callsign cannot be empty.", nameof(value));
        }

        if (value.Length > MaxCallsignLength)
        {
            throw new ArgumentException(
                $"Callsign cannot exceed {MaxCallsignLength} characters.",
                nameof(value));
        }

        foreach (var character in value)
        {
            if (!char.IsLetterOrDigit(character))
            {
                throw new ArgumentException(
                    $"Callsign contains invalid character '{character}'. Only alphanumeric characters are allowed.",
                    nameof(value));
            }
        }

        return value.ToUpperInvariant();
    }

    /// <summary>
    /// Validates a station SSID.
    /// </summary>
    public static AprsSsid Ssid(AprsSsid value) =>
        EnsureDefined(value, "SSID must be between 0 and 15.");

    /// <summary>
    /// Validates a station symbol table.
    /// </summary>
    public static SymbolTable SymbolTable(SymbolTable value) =>
        EnsureDefined(value, "Symbol table must be '/' or '\\'.");

    /// <summary>
    /// Validates a station symbol code.
    /// </summary>
    public static SymbolCode SymbolCode(SymbolCode value) =>
        EnsureDefined(value, "Symbol code must be printable ASCII (33-126).");

    /// <summary>
    /// Validates an optional station symbol overlay.
    /// </summary>
    public static SymbolCode? SymbolOverlay(SymbolCode? value) =>
        value is null ? null : EnsureDefined(value.Value, "Symbol overlay must be printable ASCII (33-126).");

    private static T EnsureDefined<T>(T value, string message)
        where T : struct, Enum
    {
        if (!Enum.IsDefined(value))
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, message);
        }

        return value;
    }
}
