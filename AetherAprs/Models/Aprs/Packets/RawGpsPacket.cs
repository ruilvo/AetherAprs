// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

namespace AetherAprs.Models.Aprs.Packets;

/// <summary>
/// A raw GPS/NMEA sentence packet (e.g. <c>$GPGGA</c>, <c>$GPRMC</c>).
/// </summary>
public sealed record RawGpsPacket : AprsPacket
{
    /// <summary>
    /// Gets the NMEA sentence text (everything after the leading <c>$</c>).
    /// </summary>
    public string Sentence { get; init; } = string.Empty;
}
