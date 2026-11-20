// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;

namespace AetherAprs.Models.Aprs;

/// <summary>
/// APRS packet types selectable in an APRS-IS server-side filter (<c>t/...</c>).
/// Each flag maps 1:1 onto the type characters accepted by javAPRSSrvr:
/// p (position), o (object), i (item), m (message), q (query), s (status),
/// t (telemetry), u (user-defined), n (NWS), w (weather).
/// </summary>
[Flags]
public enum AprsIsPacketType
{
    None = 0,
    Position = 1 << 0,
    ObjectPacket = 1 << 1,
    Item = 1 << 2,
    Message = 1 << 3,
    Query = 1 << 4,
    Status = 1 << 5,
    Telemetry = 1 << 6,
    UserDefined = 1 << 7,
    Nws = 1 << 8,
    Weather = 1 << 9,
    All = Position | ObjectPacket | Item | Message | Query | Status | Telemetry | UserDefined | Nws | Weather,
}
