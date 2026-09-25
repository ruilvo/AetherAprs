// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

namespace AetherAprs.Models.Aprs;

/// <summary>
/// An APRS message rejection packet.
/// </summary>
public sealed record MessageRejPacket : AprsPacket
{
    /// <summary>
    /// Gets the addressee callsign (the station being rejected).
    /// </summary>
    public Callsign Addressee { get; init; }

    /// <summary>
    /// Gets the message number being rejected.
    /// </summary>
    public int MessageNumber { get; init; }
}
