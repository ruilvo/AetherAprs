// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Models;

namespace AetherAprs.Data;

/// <summary>
/// Persisted parameters for one dynamic beaconing mode.
/// </summary>
public sealed class BeaconConfigRecord
{
    public DynamicBeaconMode Mode { get; set; }

    public string DisplayName { get; set; } = string.Empty;

    public int SlowIntervalSeconds { get; set; }

    public int NormalIntervalSeconds { get; set; }

    public int FastIntervalSeconds { get; set; }

    public double FastSpeedThresholdKmh { get; set; }

    public double SlowSpeedThresholdKmh { get; set; }

    public int CourseChangeThresholdDegrees { get; set; }

    public int MinimumDistanceMeters { get; set; }

    public string? BeaconComment { get; set; }
}
