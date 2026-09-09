// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

namespace AetherAprs.Models.Aprs;

/// <summary>
/// An unknown APRS packet type. Contains the raw info field.
/// </summary>
public sealed record UnknownPacket : AprsPacket
{
    /// <inheritdoc />
    public override PacketType Type => PacketType.Unknown;
}