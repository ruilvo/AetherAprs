// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

namespace AetherAprs.Models;

/// <summary>
/// Enumeration of available dynamic beaconing modes.
/// </summary>
public enum DynamicBeaconMode
{
    /// <summary>
    /// Preset configuration optimized for walking (pedestrian).
    /// </summary>
    Walk = 0,

    /// <summary>
    /// Preset configuration optimized for driving (vehicular).
    /// </summary>
    Drive = 1,

    /// <summary>
    /// Fully customizable beacon configuration.
    /// </summary>
    Custom = 2
}

/// <summary>
/// Configuration for dynamic position beaconing with intelligent transmit intervals.
/// </summary>
public sealed record BeaconConfiguration
{
    /// <summary>
    /// Identifies this beacon configuration.
    /// </summary>
    public DynamicBeaconMode Mode { get; init; }

    /// <summary>
    /// Display name for this beacon configuration (e.g., "Walking", "Driving").
    /// </summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>
    /// Interval in seconds between beacon transmissions when stationary or moving slowly.
    /// </summary>
    public int SlowIntervalSeconds { get; init; } = 1800; // 30 minutes default

    /// <summary>
    /// Interval in seconds between beacon transmissions when moving at normal speed.
    /// </summary>
    public int NormalIntervalSeconds { get; init; } = 600; // 10 minutes default

    /// <summary>
    /// Interval in seconds between beacon transmissions when moving at fast speed.
    /// </summary>
    public int FastIntervalSeconds { get; init; } = 120; // 2 minutes default

    /// <summary>
    /// Speed in km/h above which to use FastIntervalSeconds. Below this uses NormalIntervalSeconds.
    /// </summary>
    public double FastSpeedThresholdKmh { get; init; } = 40.0;

    /// <summary>
    /// Speed in km/h below which to use SlowIntervalSeconds. Between thresholds uses NormalIntervalSeconds.
    /// </summary>
    public double SlowSpeedThresholdKmh { get; init; } = 5.0;

    /// <summary>
    /// Course (heading) change in degrees that triggers immediate beacon transmission.
    /// Zero disables course change triggered beaconing.
    /// </summary>
    public int CourseChangeThresholdDegrees { get; init; } = 10;

    /// <summary>
    /// Minimum distance in meters traveled before considering a position update.
    /// Prevents beacon transmission for minor GPS jitter.
    /// </summary>
    public int MinimumDistanceMeters { get; init; } = 50;

    /// <summary>
    /// Optional comment/status text to include in beacon transmissions.
    /// </summary>
    public string? BeaconComment { get; init; }

    /// <summary>
    /// Creates a preset Walk beacon configuration optimized for pedestrian movement.
    /// </summary>
    public static BeaconConfiguration CreateWalkPreset() =>
        new()
        {
            Mode = DynamicBeaconMode.Walk,
            DisplayName = "Walk",
            SlowIntervalSeconds = 1800,    // 30 minutes
            NormalIntervalSeconds = 600,   // 10 minutes
            FastIntervalSeconds = 300,     // 5 minutes
            FastSpeedThresholdKmh = 15.0,
            SlowSpeedThresholdKmh = 2.0,
            CourseChangeThresholdDegrees = 15,
            MinimumDistanceMeters = 30,
            BeaconComment = "Walking"
        };

    /// <summary>
    /// Creates a preset Drive beacon configuration optimized for vehicular movement.
    /// </summary>
    public static BeaconConfiguration CreateDrivePreset() =>
        new()
        {
            Mode = DynamicBeaconMode.Drive,
            DisplayName = "Drive",
            SlowIntervalSeconds = 600,     // 10 minutes
            NormalIntervalSeconds = 120,   // 2 minutes
            FastIntervalSeconds = 30,      // 30 seconds
            FastSpeedThresholdKmh = 60.0,
            SlowSpeedThresholdKmh = 10.0,
            CourseChangeThresholdDegrees = 10,
            MinimumDistanceMeters = 50,
            BeaconComment = "Driving"
        };

    /// <summary>
    /// Creates a blank Custom beacon configuration that can be modified.
    /// </summary>
    public static BeaconConfiguration CreateCustomPreset() =>
        new()
        {
            Mode = DynamicBeaconMode.Custom,
            DisplayName = "Custom",
            SlowIntervalSeconds = 1800,
            NormalIntervalSeconds = 600,
            FastIntervalSeconds = 120,
            FastSpeedThresholdKmh = 40.0,
            SlowSpeedThresholdKmh = 5.0,
            CourseChangeThresholdDegrees = 10,
            MinimumDistanceMeters = 50,
            BeaconComment = "Custom"
        };
}
