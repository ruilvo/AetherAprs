// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

namespace AetherAprs.Models.Aprs.Packets;

/// <summary>
/// An APRS third-party traffic packet. The encapsulated payload is an entire
/// APRS packet (header and info field) carried inside this packet's info field.
/// </summary>
public sealed record ThirdPartyPacket : AprsPacket
{
    /// <summary>
    /// Gets the encapsulated APRS packet text (everything after the leading
    /// <c>}</c>).
    /// </summary>
    public string Encapsulated { get; init; } = string.Empty;
}
