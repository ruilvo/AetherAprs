// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

namespace AetherAprs.Models.Aprs;

/// <summary>
/// An APRS message acknowledgment packet.
/// </summary>
public sealed record MessageAckPacket : AprsPacket
{
    /// <summary>
    /// Gets the addressee callsign (the station being acknowledged).
    /// </summary>
    public Callsign Addressee { get; init; }

    /// <summary>
    /// Gets the message number being acknowledged.
    /// </summary>
    public int MessageNumber { get; init; }
}
