// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;

namespace AetherAprs.Helpers;

/// <summary>
/// Computes the APRS-IS passcode from a callsign using the standard XOR-based algorithm.
/// </summary>
public static class AprsPasscode
{
    /// <summary>
    /// Computes the APRS-IS passcode for the given callsign base (no SSID).
    /// </summary>
    /// <param name="callsign">The callsign base (e.g. "N0CALL", without SSID).</param>
    /// <returns>The numeric passcode as a string.</returns>
    public static string Compute(string callsign)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(callsign);

        var upper = callsign.ToUpperInvariant();
        int hash = 0x73e2; // Initial value
        int i = 0;

        while (i < upper.Length)
        {
            hash ^= upper[i] << 8;
            if (i + 1 < upper.Length)
            {
                hash ^= upper[i + 1];
            }

            i += 2;
        }

        return (hash & 0x7fff).ToString();
    }
}