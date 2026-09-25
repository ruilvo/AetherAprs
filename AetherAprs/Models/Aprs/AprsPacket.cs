// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;

namespace AetherAprs.Models.Aprs;

/// <summary>
/// Abstract base record for all APRS packets.
/// </summary>
public abstract record AprsPacket
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
    /// Gets the digipeater path (list of callsigns between source and destination).
    /// Empty list means no digipeaters in path.
    /// </summary>
    public IReadOnlyList<Callsign> Path { get; init; } = Array.Empty<Callsign>();

    /// <summary>
    /// Gets the timestamp, if available from the packet.
    /// </summary>
    public DateTimeOffset? Timestamp { get; init; }

    /// <summary>
    /// Gets the raw APRS info field as a string.
    /// </summary>
    public string Raw { get; init; } = string.Empty;

    /// <summary>
    /// Gets the original source identifier when it is not a standard callsign.
    /// </summary>
    public string? RawSource { get; init; }
}
