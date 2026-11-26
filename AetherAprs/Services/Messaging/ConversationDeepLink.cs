// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Models.Aprs;

namespace AetherAprs.Services.Messaging;

/// <summary>
/// Reads the peer address that a message notification carries to the platform
/// entry point.
/// </summary>
/// <remarks>
/// The peer is an <see cref="AprsAddress"/>, not a <see cref="Callsign"/>: a message
/// can be addressed to a nine-character tactical address such as "APRS2SOTA", so
/// applying the six-character callsign shape here would make those conversations
/// unreachable from their notification.
///
/// The activity that receives this value is exported, so the value is untrusted
/// input. It is bounded before any parsing work happens and anything that is not a
/// plain address is rejected rather than trimmed into shape, so a malformed deep
/// link cannot select a conversation the operator did not tap.
/// </remarks>
public static class ConversationDeepLink
{
    /// <summary>
    /// Longest accepted value: the full APRS addressee width plus a two-digit SSID.
    /// </summary>
    public const int MaximumLength = AprsAddress.MaximumTextLength;

    /// <summary>Tries to read the peer address from a stored callsign extra.</summary>
    /// <param name="value">The raw intent extra, possibly null.</param>
    /// <param name="peer">The validated address when the method returns true.</param>
    /// <returns>True when the value is an address that can identify a conversation.</returns>
    public static bool TryParsePeer(string? value, out AprsAddress peer)
    {
        peer = default;
        if (value is null || value.Length is 0 or > MaximumLength)
        {
            return false;
        }

        // Only the characters an address is made of. Trimming would make
        // "N0CALL\r\n" acceptable, and an extra from another application should not
        // be repaired into a valid address.
        foreach (char character in value)
        {
            if (character is not (>= '0' and <= '9' or >= 'A' and <= 'Z' or >= 'a' and <= 'z' or '-'))
            {
                return false;
            }
        }

        if (!AprsAddress.TryParse(value, out AprsAddress address))
        {
            return false;
        }

        peer = address;
        return true;
    }
}
