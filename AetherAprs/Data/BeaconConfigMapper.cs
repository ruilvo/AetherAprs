// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Models;

namespace AetherAprs.Data;

internal static class BeaconConfigMapper
{
    public static BeaconConfigRecord ToRecord(BeaconConfig config)
    {
        return new BeaconConfigRecord
        {
            Mode = config.Mode,
            DisplayName = config.DisplayName,
            SlowIntervalSeconds = config.SlowIntervalSeconds,
            NormalIntervalSeconds = config.NormalIntervalSeconds,
            FastIntervalSeconds = config.FastIntervalSeconds,
            FastSpeedThresholdKmh = config.FastSpeedThresholdKmh,
            SlowSpeedThresholdKmh = config.SlowSpeedThresholdKmh,
            CourseChangeThresholdDegrees = config.CourseChangeThresholdDegrees,
            MinimumDistanceMeters = config.MinimumDistanceMeters,
            BeaconComment = config.BeaconComment
        };
    }

    public static BeaconConfig ToConfig(BeaconConfigRecord record)
    {
        return new BeaconConfig
        {
            Mode = record.Mode,
            DisplayName = record.DisplayName,
            SlowIntervalSeconds = record.SlowIntervalSeconds,
            NormalIntervalSeconds = record.NormalIntervalSeconds,
            FastIntervalSeconds = record.FastIntervalSeconds,
            FastSpeedThresholdKmh = record.FastSpeedThresholdKmh,
            SlowSpeedThresholdKmh = record.SlowSpeedThresholdKmh,
            CourseChangeThresholdDegrees = record.CourseChangeThresholdDegrees,
            MinimumDistanceMeters = record.MinimumDistanceMeters,
            BeaconComment = record.BeaconComment
        };
    }

    public static void CopyTo(BeaconConfigRecord target, BeaconConfig config)
    {
        target.DisplayName = config.DisplayName;
        target.SlowIntervalSeconds = config.SlowIntervalSeconds;
        target.NormalIntervalSeconds = config.NormalIntervalSeconds;
        target.FastIntervalSeconds = config.FastIntervalSeconds;
        target.FastSpeedThresholdKmh = config.FastSpeedThresholdKmh;
        target.SlowSpeedThresholdKmh = config.SlowSpeedThresholdKmh;
        target.CourseChangeThresholdDegrees = config.CourseChangeThresholdDegrees;
        target.MinimumDistanceMeters = config.MinimumDistanceMeters;
        target.BeaconComment = config.BeaconComment;
    }
}
