// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

namespace AetherAprs.Models.Aprs;

/// <summary>
/// An APRS status report packet.
/// </summary>
public sealed record StatusPacket : AprsPacket
{
    /// <inheritdoc />
    public override PacketType Type => PacketType.Status;

    /// <summary>
    /// Gets the status text.
    /// </summary>
    public string Text { get; init; } = string.Empty;
}