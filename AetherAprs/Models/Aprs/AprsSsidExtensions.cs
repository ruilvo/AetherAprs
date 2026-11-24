// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

namespace AetherAprs.Models.Aprs;

/// <summary>
/// Display formatting helpers for APRS station identifiers.
/// </summary>
public static class AprsSsidExtensions
{
    /// <summary>
    /// Formats a station identifier for display by appending the SSID when it is
    /// set and is not the primary station suffix.
    /// </summary>
    /// <param name="ssid">The SSID to append, or <see langword="null"/> when the identifier has none.</param>
    /// <param name="baseCallsign">The base callsign without an SSID suffix.</param>
    /// <returns>
    /// <paramref name="baseCallsign"/> for a missing SSID or
    /// <see cref="AprsSsid.PrimaryStation"/>, otherwise <c>{baseCallsign}-{ssid}</c>.
    /// </returns>
    /// <remarks>
    /// This is a display-only helper that deliberately renders SSID 0 as the bare base
    /// callsign. Never use its result as a dictionary key or for equality comparisons;
    /// keep the structured base/SSID pair for identity.
    /// </remarks>
    public static string FormatCallsign(string baseCallsign, AprsSsid? ssid)
    {
        return ssid is null or AprsSsid.PrimaryStation
            ? baseCallsign
            : $"{baseCallsign}-{(byte)ssid.Value}";
    }
}
