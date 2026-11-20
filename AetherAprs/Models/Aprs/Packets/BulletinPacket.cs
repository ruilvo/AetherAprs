// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

namespace AetherAprs.Models.Aprs.Packets;

/// <summary>
/// An APRS bulletin packet. Bulletins are broadcast messages addressed to a
/// <c>BLN</c>-prefixed name rather than an individual callsign.
/// </summary>
public sealed record BulletinPacket : AprsPacket
{
    /// <summary>
    /// Gets the bulletin name (the raw addressee, e.g. <c>BLN0SPCL</c>).
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Gets the bulletin text.
    /// </summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>
    /// Gets the optional message number (from the <c>{nn}</c> suffix).
    /// </summary>
    public int? MessageNumber { get; init; }
}
