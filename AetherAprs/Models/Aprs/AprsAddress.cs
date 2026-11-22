// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;

namespace AetherAprs.Models.Aprs;

/// <summary>
/// Represents an APRS addressee, which can be an amateur radio callsign (with optional SSID)
/// or a tactical identifier/software name up to 9 characters.
/// More lenient than <see cref="Callsign"/> to accommodate tactical addresses like "APRS2SOTA",
/// "EMAIL", "NWS-", etc.
/// </summary>
public readonly record struct AprsAddress
{
    /// <summary>
    /// Gets the base address (1-9 characters, letters and digits).
    /// </summary>
    public string Base { get; }

    /// <summary>
    /// Gets the optional SSID (0-15), or null if not specified.
    /// </summary>
    public int? Ssid { get; }

    /// <summary>
    /// Initializes a new instance of <see cref="AprsAddress"/>.
    /// </summary>
    /// <param name="addressBase">The base address (1-9 alphanumeric characters).</param>
    /// <param name="ssid">Optional SSID (0-15).</param>
    /// <exception cref="ArgumentException">Thrown if <paramref name="addressBase"/> is invalid.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if <paramref name="ssid"/> is out of range.</exception>
    public AprsAddress(string addressBase, int? ssid = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(addressBase);
        addressBase = addressBase.Trim().ToUpperInvariant();

        if (addressBase.Length < 1 || addressBase.Length > 9)
        {
            throw new ArgumentException($"APRS address base must be 1-9 characters, got '{addressBase}'.", nameof(addressBase));
        }

        foreach (var character in addressBase)
        {
            if (!((character >= 'A' && character <= 'Z') || (character >= '0' && character <= '9') || character == '-'))
            {
                throw new ArgumentException("APRS address base must contain only ASCII letters, digits, and hyphens.", nameof(addressBase));
            }
        }

        if (ssid is < 0 or > 15)
        {
            throw new ArgumentOutOfRangeException(nameof(ssid), ssid, "SSID must be between 0 and 15.");
        }

        Base = addressBase;
        Ssid = ssid;
    }

    /// <summary>
    /// Tries to parse an APRS address string such as "APRS2SOTA" or "N0CALL-1".
    /// </summary>
    public static bool TryParse(string? value, out AprsAddress address)
    {
        address = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        value = value.Trim().ToUpperInvariant();
        string addressBase;
        int? ssid = null;

        var dashIndex = value.IndexOf('-');
        if (dashIndex >= 0)
        {
            addressBase = value[..dashIndex];
            var ssidPart = value[(dashIndex + 1)..];
            if (!int.TryParse(ssidPart, out var ssidValue) || ssidValue is < 0 or > 15)
            {
                return false;
            }

            ssid = ssidValue;
        }
        else
        {
            addressBase = value;
        }

        if (addressBase.Length < 1 || addressBase.Length > 9)
        {
            return false;
        }

        foreach (var character in addressBase)
        {
            if (!((character >= 'A' && character <= 'Z') || (character >= '0' && character <= '9') || character == '-'))
            {
                return false;
            }
        }

        address = new AprsAddress(addressBase, ssid);
        return true;
    }

    /// <summary>
    /// Creates an AprsAddress from a Callsign.
    /// </summary>
    public static AprsAddress FromCallsign(Callsign callsign)
    {
        return new AprsAddress(callsign.Base, callsign.Ssid);
    }

    /// <summary>
    /// Tries to convert this AprsAddress to a strict Callsign.
    /// Returns false if the address doesn't meet callsign requirements (2-6 chars, no hyphens except before SSID).
    /// </summary>
    public bool TryToCallsign(out Callsign callsign)
    {
        callsign = default;
        
        if (Base.Contains('-'))
        {
            return false;
        }

        if (Callsign.TryParse(ToString(), out callsign))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Returns the string representation: e.g. "APRS2SOTA" or "N0CALL-1".
    /// </summary>
    public override string ToString()
    {
        return Ssid is > 0 ? $"{Base}-{Ssid.Value}" : Base;
    }
}
