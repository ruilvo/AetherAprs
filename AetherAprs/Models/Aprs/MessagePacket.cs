// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

namespace AetherAprs.Models.Aprs;

/// <summary>
/// An APRS message packet.
/// </summary>
public sealed record MessagePacket : AprsPacket
{
    /// <summary>
    /// Gets the addressee callsign.
    /// </summary>
    public Callsign Addressee { get; init; }

    /// <summary>
    /// Gets the message text.
    /// </summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>
    /// Gets the optional message number (from {nn} suffix).
    /// </summary>
    public int? MessageNumber { get; init; }
}