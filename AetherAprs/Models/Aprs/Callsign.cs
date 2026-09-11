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
    /// <param name="base">The base callsign (2-6 alphanumeric characters).</param>
    /// <param name="ssid">Optional SSID (0-15).</param>
    /// <exception cref="ArgumentException">Thrown if <paramref name="base"/> is invalid.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if <paramref name="ssid"/> is out of range.</exception>
    public Callsign(string @base, int? ssid = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(@base);
        @base = @base.Trim().ToUpperInvariant();

        if (@base.Length < 2 || @base.Length > 6)
        {
            throw new ArgumentException($"Callsign base must be 2-6 characters, got '{@base}'.", nameof(@base));
        }

        if (ssid is < 0 or > 15)
        {
            throw new ArgumentOutOfRangeException(nameof(ssid), ssid, "SSID must be between 0 and 15.");
        }

        Base = @base;
        Ssid = ssid;
    }

    /// <summary>
    /// Returns the string representation: e.g. "N0CALL" or "N0CALL-1".
    /// </summary>
    public override string ToString()
    {
        return Ssid is > 0 ? $"{Base}-{Ssid.Value}" : Base;
    }
}
