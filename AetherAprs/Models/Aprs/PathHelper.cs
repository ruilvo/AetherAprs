// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Linq;

namespace AetherAprs.Models.Aprs;

/// <summary>
/// Helper methods for working with APRS digipeater paths.
/// </summary>
public static class PathHelper
{
    /// <summary>
    /// Parses a digipeater path string (e.g., "WIDE1-1,WIDE2-1") into a list of callsigns.
    /// </summary>
    /// <param name="pathString">The path string, with callsigns separated by commas.</param>
    /// <returns>A list of callsigns representing the path, or an empty list if the path is null/empty.</returns>
    /// <exception cref="ArgumentException">Thrown if the path string contains invalid callsigns.</exception>
    public static IReadOnlyList<Callsign> ParsePath(string? pathString)
    {
        if (string.IsNullOrWhiteSpace(pathString))
        {
            return Array.Empty<Callsign>();
        }

        var parts = pathString.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var result = new List<Callsign>(parts.Length);

        foreach (var part in parts)
        {
            var callsign = ParseCallsign(part);
            result.Add(callsign);
        }

        return result;
    }

    /// <summary>
    /// Parses a single callsign string (e.g., "WIDE1-1" or "N0CALL") into a Callsign object.
    /// </summary>
    /// <param name="callsignString">The callsign string, optionally with SSID (e.g., "N0CALL-5").</param>
    /// <returns>A Callsign object.</returns>
    /// <exception cref="ArgumentException">Thrown if the callsign string is invalid.</exception>
    private static Callsign ParseCallsign(string callsignString)
    {
        if (string.IsNullOrWhiteSpace(callsignString))
        {
            throw new ArgumentException("Callsign cannot be empty.", nameof(callsignString));
        }

        var parts = callsignString.Split('-', 2);
        var baseCallsign = parts[0].Trim().ToUpperInvariant();

        if (baseCallsign.Length == 0 || baseCallsign.Length > 6)
        {
            throw new ArgumentException($"Invalid callsign base '{baseCallsign}': must be 1-6 characters.", nameof(callsignString));
        }

        int? ssid = null;
        if (parts.Length > 1)
        {
            if (!int.TryParse(parts[1], out var ssidValue) || ssidValue < 0 || ssidValue > 15)
            {
                throw new ArgumentException($"Invalid SSID '{parts[1]}': must be 0-15.", nameof(callsignString));
            }
            ssid = ssidValue;
        }

        return new Callsign(baseCallsign, ssid);
    }

    /// <summary>
    /// Formats a path (list of callsigns) as a string (e.g., "WIDE1-1,WIDE2-1").
    /// </summary>
    /// <param name="path">The path to format.</param>
    /// <returns>A comma-separated string of callsigns.</returns>
    public static string FormatPath(IReadOnlyList<Callsign> path)
    {
        if (path == null || path.Count == 0)
        {
            return string.Empty;
        }

        return string.Join(",", path.Select(c => c.ToString()));
    }

    /// <summary>
    /// Checks if a callsign matches a digipeater alias pattern.
    /// </summary>
    /// <param name="callsign">The callsign to check.</param>
    /// <param name="alias">The alias pattern (e.g., "WIDE1" or "WIDE2").</param>
    /// <returns>True if the callsign base matches the alias.</returns>
    public static bool MatchesAlias(Callsign callsign, string alias)
    {
        return string.Equals(callsign.Base, alias, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Decrements the SSID of a callsign (used in digipeater hop counting).
    /// </summary>
    /// <param name="callsign">The callsign to decrement.</param>
    /// <returns>A new callsign with SSID decremented by 1, or null if SSID is already 0 or null.</returns>
    public static Callsign? DecrementSsid(Callsign callsign)
    {
        if (!callsign.Ssid.HasValue || callsign.Ssid.Value <= 0)
        {
            return null;
        }

        return new Callsign(callsign.Base, callsign.Ssid.Value - 1);
    }
}
