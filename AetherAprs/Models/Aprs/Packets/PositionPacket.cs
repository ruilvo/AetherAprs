// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

namespace AetherAprs.Models.Aprs.Packets;

/// <summary>
/// An APRS position report packet.
/// </summary>
public sealed record PositionPacket : PositionPacketBase
{
    /// <summary>
    /// Gets the wire encoding used for this position (uncompressed, compressed
    /// base-91, or Mic-E). Serializers honor this when emitting the info field.
    /// </summary>
    public PositionFormat Format { get; init; } = PositionFormat.Uncompressed;
}
