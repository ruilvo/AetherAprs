// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;

namespace AetherAprs.Models.Aprs;

/// <summary>
/// Represents an APRS addressee: the destination field of a message, ack, reject
/// or bulletin. An addressee is usually a callsign, but it does not have to be one;
/// the APRS message field is nine arbitrary characters, so tactical addresses such
/// as "APRS2SOTA", "EMAIL" or "NWS-" are equally valid recipients.
/// </summary>
/// <remarks>
/// This type is deliberately more lenient than <see cref="Callsign"/>. Use it for
/// anything a message is addressed to, and <see cref="Callsign"/> only where the
/// AX.25 station field requires a real callsign (two to six characters plus SSID).
/// </remarks>
public readonly record struct AprsAddress
{
    private const int MinimumBaseLength = 1;

    /// <summary>
    /// Longest accepted base address: the full width of the APRS message addressee
    /// field, which is wider than the six-character callsign base.
    /// </summary>
    public const int MaximumBaseLength = 9;

    /// <summary>
    /// Longest accepted text form: a full-width base, the SSID separator and a
    /// two-digit SSID.
    /// </summary>
    public const int MaximumTextLength = MaximumBaseLength + 1 + 2;

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

        if (addressBase.Length is < MinimumBaseLength or > MaximumBaseLength)
        {
            throw new ArgumentException($"APRS address base must be 1-9 characters, got '{addressBase}'.", nameof(addressBase));
        }

        if (!HasOnlyBaseCharacters(addressBase))
        {
            throw new ArgumentException("APRS address base must contain only ASCII letters, digits, and hyphens.", nameof(addressBase));
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

        if (addressBase.Length is < MinimumBaseLength or > MaximumBaseLength
            || !HasOnlyBaseCharacters(addressBase))
        {
            return false;
        }

        address = new AprsAddress(addressBase, ssid);
        return true;
    }

    private static bool HasOnlyBaseCharacters(string value)
    {
        foreach (var character in value)
        {
            if (!IsBaseCharacter(character))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsBaseCharacter(char character) =>
        character is >= 'A' and <= 'Z' or >= '0' and <= '9' or '-';

    /// <summary>
    /// Creates an AprsAddress from a Callsign.
    /// </summary>
    public static AprsAddress FromCallsign(Callsign callsign)
    {
        return new AprsAddress(callsign.Base, callsign.Ssid);
    }

    /// <summary>
    /// Creates an address from the two values storage keeps: a base and an SSID
    /// where zero also stands for "no SSID".
    /// </summary>
    /// <remarks>
    /// APRS treats "N0CALL" and "N0CALL-0" as the same station, and the message
    /// addressee field cannot express the difference. A stored zero therefore
    /// restores the base-only address. Keeping zero would make the reloaded address
    /// unequal to the one the operator entered (this type distinguishes null from
    /// zero) and split a conversation into two threads.
    /// </remarks>
    public static AprsAddress FromStored(string baseAddress, int ssid) =>
        new(baseAddress, ssid == 0 ? null : ssid);

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
    /// <remarks>
    /// SSID 0 and no SSID both format as the base address alone. Use the
    /// <see cref="AprsAddress"/> value itself, not this string, as a dictionary key or
    /// identity because those two values are not equal.
    /// </remarks>
    public override string ToString()
    {
        return Ssid is > 0 ? $"{Base}-{Ssid.Value}" : Base;
    }
}
