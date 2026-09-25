// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

namespace AetherAprs.Configuration;

/// <summary>
/// Defines time range presets for displaying packets in the list and on the map.
/// </summary>
public enum PacketDisplayTimeRange
{
    /// <summary>
    /// Show packets from the last hour
    /// </summary>
    LastHour,

    /// <summary>
    /// Show packets from the last 24 hours
    /// </summary>
    LastDay,

    /// <summary>
    /// Show packets from the last 7 days
    /// </summary>
    LastWeek,

    /// <summary>
    /// Show packets from the last 30 days
    /// </summary>
    LastMonth,

    /// <summary>
    /// Show all packets (no time limit)
    /// </summary>
    All,

    /// <summary>
    /// Use custom time limit specified by CustomTimeRangeHours
    /// </summary>
    Custom
}
