// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;

namespace AetherAprs.Models.Aprs;

/// <summary>
/// Abstract base record for all APRS packets.
/// </summary>
public abstract record AprsPacket : IAprsPacket
{
    /// <summary>
    /// Gets the source callsign.
    /// </summary>
    public Callsign Source { get; init; }

    /// <summary>
    /// Gets the destination callsign.
    /// </summary>
    public Callsign Destination { get; init; }

    /// <summary>
    /// Gets the timestamp, if available from the packet.
    /// </summary>
    public DateTimeOffset? Timestamp { get; init; }

    /// <summary>
    /// Gets the packet type.
    /// </summary>
    public abstract PacketType Type { get; }

    /// <summary>
    /// Gets the raw APRS info field as a string.
    /// </summary>
    public string Raw { get; init; } = string.Empty;
}