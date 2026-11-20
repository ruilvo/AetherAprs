// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

namespace AetherAprs.Models.Aprs.Packets;

/// <summary>
/// An APRS object report packet.
/// </summary>
public sealed record ObjectPacket : PositionPacketBase
{
    /// <summary>
    /// Gets the object name (up to 9 characters).
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Gets whether the object is live (<c>true</c>) or killed (<c>false</c>).
    /// </summary>
    public bool IsLive { get; init; } = true;

    /// <summary>
    /// Gets the raw 7-character <c>DDHHMMz</c> timestamp string from the object
    /// report. Object timestamps encode day/hour/minute without a year, so they
    /// cannot be represented as a <see cref="AprsPacket.Timestamp"/>.
    /// </summary>
    public string? RawTimestamp { get; init; }
}
