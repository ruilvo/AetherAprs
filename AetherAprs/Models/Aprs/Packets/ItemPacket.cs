// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

namespace AetherAprs.Models.Aprs.Packets;

/// <summary>
/// An APRS item report packet.
/// </summary>
public sealed record ItemPacket : PositionPacketBase
{
    /// <summary>
    /// Gets the item name (3-9 characters).
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Gets whether the item is live (<c>true</c>) or killed (<c>false</c>).
    /// </summary>
    public bool IsLive { get; init; } = true;
}
