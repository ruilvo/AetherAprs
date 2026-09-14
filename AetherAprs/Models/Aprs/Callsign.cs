// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;

namespace AetherAprs.Models.Aprs;

/// <summary>
/// Represents an Amateur radio callsign with optional SSID.
/// </summary>
public readonly record struct Callsign
{
    /// <summary>
    /// Gets the base callsign (2-6 characters, letters and digits).
    /// </summary>
    public string Base { get; }

    /// <summary>
    /// Gets the optional SSID (0-15), or null if not specified.
    /// </summary>
    public int? Ssid { get; }

    /// <summary>
    /// Initializes a new instance of <see cref="Callsign"/>.
    /// </summary>
    /// <param name="callsignBase">The base callsign (2-6 alphanumeric characters).</param>
    /// <param name="ssid">Optional SSID (0-15).</param>
    /// <exception cref="ArgumentException">Thrown if <paramref name="callsignBase"/> is invalid.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if <paramref name="ssid"/> is out of range.</exception>
    public Callsign(string callsignBase, int? ssid = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(callsignBase);
        callsignBase = callsignBase.Trim().ToUpperInvariant();

        if (callsignBase.Length < 2 || callsignBase.Length > 6)
        {
            throw new ArgumentException($"Callsign base must be 2-6 characters, got '{callsignBase}'.", nameof(callsignBase));
        }

        foreach (var character in callsignBase)
        {
            if (!((character >= 'A' && character <= 'Z') || (character >= '0' && character <= '9')))
            {
                throw new ArgumentException("Callsign base must contain only ASCII letters and digits.", nameof(callsignBase));
            }
        }

        if (ssid is < 0 or > 15)
        {
            throw new ArgumentOutOfRangeException(nameof(ssid), ssid, "SSID must be between 0 and 15.");
        }

        Base = callsignBase;
        Ssid = ssid;
    }

    /// <summary>
    /// Tries to parse a callsign string such as "N0CALL" or "N0CALL-1".
    /// </summary>
    public static bool TryParse(string? value, out Callsign callsign)
    {
        callsign = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        value = value.Trim().ToUpperInvariant();
        string callsignBase;
        int? ssid = null;

        var dashIndex = value.IndexOf('-');
        if (dashIndex >= 0)
        {
            callsignBase = value[..dashIndex];
            var ssidPart = value[(dashIndex + 1)..];
            if (!int.TryParse(ssidPart, out var ssidValue) || ssidValue is < 0 or > 15)
            {
                return false;
            }

            ssid = ssidValue;
        }
        else
        {
            callsignBase = value;
        }

        if (callsignBase.Length < 2 || callsignBase.Length > 6)
        {
            return false;
        }

        foreach (var character in callsignBase)
        {
            if (!((character >= 'A' && character <= 'Z') || (character >= '0' && character <= '9')))
            {
                return false;
            }
        }

        callsign = new Callsign(callsignBase, ssid);
        return true;
    }

    /// <summary>
    /// Returns the string representation: e.g. "N0CALL" or "N0CALL-1".
    /// </summary>
    public override string ToString()
    {
        return Ssid is > 0 ? $"{Base}-{Ssid.Value}" : Base;
    }
}
