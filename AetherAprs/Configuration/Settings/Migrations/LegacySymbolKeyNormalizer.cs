// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using AetherAprs.Models.Aprs;
using Microsoft.Extensions.Logging;

namespace AetherAprs.Configuration.Settings.Migrations;

/// <summary>
/// Accepts the historical single-character symbol spellings
/// (<c>SymbolTableCharacter</c>, <c>SymbolCodeCharacter</c>,
/// <c>SymbolOverlayCharacter</c> and their <c>Default…</c> variants) and
/// rewrites them as the strongly typed values stored under
/// <c>SymbolTable</c>, <c>SymbolCode</c>, and <c>SymbolOverlay</c>.
/// </summary>
/// <remarks>
/// This runs on every load, not only during schema migration, because saves
/// still emit the historical JSON property names for compatibility. It is
/// idempotent, so a document that has already been normalized is unchanged.
/// Values that cannot be converted are logged and dropped so the property
/// falls back to its default instead of failing the whole load.
/// </remarks>
public static class LegacySymbolKeyNormalizer
{
    private static readonly string[] LegacyTableKeys =
    [
        "Aprs:SymbolTableCharacter",
        "Aprs:DefaultSymbolTableCharacter",
    ];

    private static readonly string[] LegacyCodeKeys =
    [
        "Aprs:SymbolCodeCharacter",
        "Aprs:DefaultSymbolCodeCharacter",
    ];

    private static readonly string[] LegacyOverlayKeys =
    [
        "Aprs:SymbolOverlayCharacter",
        "Aprs:DefaultSymbolOverlayCharacter",
    ];

    /// <summary>
    /// Rewrites legacy symbol keys in <paramref name="values"/> in place.
    /// </summary>
    /// <param name="values">Flattened settings key/value pairs.</param>
    /// <param name="logger">Logger for skipped values.</param>
    public static void Normalize(IDictionary<string, string?> values, ILogger logger)
    {
        MigrateTable(values, logger);
        MigrateSymbolCode(values, LegacyCodeKeys, "Aprs:SymbolCode", logger);
        MigrateSymbolCode(values, LegacyOverlayKeys, "Aprs:SymbolOverlay", logger);
    }

    private static void MigrateTable(IDictionary<string, string?> values, ILogger logger)
    {
        foreach (var key in LegacyTableKeys)
        {
            if (!values.TryGetValue(key, out var raw) || raw is null)
            {
                continue;
            }

            values.Remove(key);
            if (values.ContainsKey("Aprs:SymbolTable"))
            {
                continue;
            }

            var converted = raw switch
            {
                "/" => SymbolTable.Primary.ToString(),
                "\\" => SymbolTable.Alternate.ToString(),
                // A value written by a previous version of this build is
                // already the enum name; keep it.
                _ when Enum.TryParse<SymbolTable>(raw, ignoreCase: true, out var parsed)
                    && Enum.IsDefined(parsed) => parsed.ToString(),
                _ => null,
            };

            if (converted is null)
            {
                logger.LogWarning(
                    "Invalid APRS symbol table value '{Value}' for key {Key}; using default.",
                    raw,
                    key);
                continue;
            }

            values["Aprs:SymbolTable"] = converted;
        }
    }

    private static void MigrateSymbolCode(
        IDictionary<string, string?> values,
        string[] legacyKeys,
        string targetKey,
        ILogger logger)
    {
        foreach (var key in legacyKeys)
        {
            if (!values.TryGetValue(key, out var raw) || raw is null)
            {
                continue;
            }

            values.Remove(key);
            if (values.ContainsKey(targetKey))
            {
                continue;
            }

            // Single character form: convert to the enum name.
            if (raw.Length == 1)
            {
                try
                {
                    values[targetKey] = raw[0].ToSymbolCode().ToString();
                    continue;
                }
                catch (Exception ex) when (ex is InvalidOperationException or ArgumentOutOfRangeException)
                {
                    logger.LogWarning(
                        ex,
                        "Invalid APRS symbol value '{Value}' for key {Key}; using default.",
                        raw,
                        key);
                    continue;
                }
            }

            // Already an enum name written by a previous version of this build.
            if (Enum.TryParse<SymbolCode>(raw, ignoreCase: true, out var parsed)
                && Enum.IsDefined(parsed))
            {
                values[targetKey] = parsed.ToString();
                continue;
            }

            logger.LogWarning(
                "Invalid APRS symbol value '{Value}' for key {Key}; using default.",
                raw,
                key);
        }
    }
}
