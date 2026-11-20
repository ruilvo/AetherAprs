// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

namespace AetherAprs.Models.Aprs.Packets;

/// <summary>
/// An APRS query packet (e.g. <c>?APRS?</c>, <c>?IGATE?</c>).
/// </summary>
public sealed record QueryPacket : AprsPacket
{
    /// <summary>
    /// Gets the query text (everything after the leading <c>?</c>).
    /// </summary>
    public string Query { get; init; } = string.Empty;
}
