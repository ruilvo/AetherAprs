// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

namespace AetherAprs.Models.Aprs;

/// <summary>
/// Defines the type of an APRS packet.
/// </summary>
public enum PacketType
{
    /// <summary>Position report (e.g. !, =, @, / formats).</summary>
    Position,

    /// <summary>APRS message (format: :ADDRESSEE:message text).</summary>
    Message,

    /// <summary>Status report (format: >status text).</summary>
    Status,

    /// <summary>Weather report (format: _weather data).</summary>
    Weather,

    /// <summary>Packet type could not be determined.</summary>
    Unknown
}