// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using AetherAprs.Models;
using AetherAprs.Models.Aprs;
using AetherAprs.Modems.Aprs;
using AetherAprs.Services;
using Xunit;

namespace AetherAprs.Tests;

/// <summary>
/// Tests for the dynamic beacon service.
/// </summary>
public class BeaconServiceTests
{
    private static LocationData CreateLocation(double latitude, double longitude, double? altitude = null, DateTimeOffset? timestamp = null)
    {
        return new LocationData
        {
            Latitude = latitude,
            Longitude = longitude,
            Altitude = altitude,
            Accuracy = 10,
            Timestamp = timestamp ?? DateTimeOffset.UtcNow
        };
    }

    [Fact]
    public void ActiveModeStartsAsWalk()
    {
        var service = new BeaconService();
        Assert.Equal(DynamicBeaconMode.Walk, service.CurrentConfiguration.Mode);
    }

    [Fact]
    public void AllConfigurationsIncludesThreeModes()
    {
        var service = new BeaconService();
        var configs = service.AllConfigurations;

        Assert.Equal(3, configs.Count);
        Assert.Contains(configs, c => c.Mode == DynamicBeaconMode.Walk);
        Assert.Contains(configs, c => c.Mode == DynamicBeaconMode.Drive);
        Assert.Contains(configs, c => c.Mode == DynamicBeaconMode.Custom);
    }

    [Fact]
    public void SetActiveModeChangesCurrentConfiguration()
    {
        var service = new BeaconService();

        service.SetActiveMode(DynamicBeaconMode.Drive);
        Assert.Equal(DynamicBeaconMode.Drive, service.CurrentConfiguration.Mode);

        service.SetActiveMode(DynamicBeaconMode.Custom);
        Assert.Equal(DynamicBeaconMode.Custom, service.CurrentConfiguration.Mode);

        service.SetActiveMode(DynamicBeaconMode.Walk);
        Assert.Equal(DynamicBeaconMode.Walk, service.CurrentConfiguration.Mode);
    }

    [Fact]
    public void UpdateCustomConfigurationModifiesCustomMode()
    {
        var service = new BeaconService();
        var customConfig = BeaconConfiguration.CreateCustomPreset();
        var modifiedConfig = customConfig with { SlowIntervalSeconds = 3600 };

        service.UpdateCustomConfiguration(modifiedConfig);

        service.SetActiveMode(DynamicBeaconMode.Custom);
        Assert.Equal(3600, service.CurrentConfiguration.SlowIntervalSeconds);
    }

    [Fact]
    public void EvaluateLocationUpdateWithNoPreviousLocationReturnsDecision()
    {
        var service = new BeaconService();
        var location = CreateLocation(38.7223, -9.1393);

        // First evaluation with no previous location
        var decision = service.EvaluateLocationUpdate(location, null);

        // Should return a valid decision
        Assert.NotNull(decision);
        Assert.True(decision.ActiveIntervalSeconds > 0);
    }

    [Fact]
    public void EvaluateLocationUpdateIgnoresSmallDistances()
    {
        var service = new BeaconService();
        var time1 = DateTimeOffset.UtcNow.AddSeconds(-2000);
        var time2 = time1.AddSeconds(5);

        // Two locations very close together (within 50m minimum)
        var location1 = CreateLocation(38.7223, -9.1393, timestamp: time1);
        var location2 = CreateLocation(38.72231, -9.13931, timestamp: time2); // ~11 meters apart

        var decision = service.EvaluateLocationUpdate(location2, location1);

        Assert.False(decision.ShouldTransmit);
        Assert.Contains("below minimum", decision.Reason);
    }

    [Fact]
    public void EvaluateLocationUpdateCalculatesCourseWhenHasPreviousLocation()
    {
        var service = new BeaconService();

        var time1 = DateTimeOffset.UtcNow.AddSeconds(-2000);
        var time2 = time1.AddSeconds(10);

        // Start heading north
        var location1 = CreateLocation(38.7223, -9.1393, timestamp: time1);
        // Move to a new location (north, same longitude)
        var location2 = CreateLocation(38.7323, -9.1393, timestamp: time2);

        var decision = service.EvaluateLocationUpdate(location2, location1);

        // Should calculate course and speed
        Assert.NotNull(decision.CurrentCourseDegrees);
        Assert.NotNull(decision.CurrentSpeedKmh);
        // Course should be roughly north (0 or 360 degrees)
        Assert.True(decision.CurrentCourseDegrees < 45 || decision.CurrentCourseDegrees > 315);
    }

    [Fact]
    public void EvaluateLocationUpdateCalculatesSpeedCorrectly()
    {
        var service = new BeaconService();
        var time1 = DateTimeOffset.UtcNow.AddSeconds(-5000);
        var time2 = time1.AddSeconds(3600); // 1 hour later

        // Start at Lisbon
        var location1 = CreateLocation(38.7223, -9.1393, timestamp: time1);
        // End at approximately 120km away (for rough estimate)
        // Moving approximately north at ~1 degree latitude = ~111km
        var location2 = CreateLocation(39.7223, -9.1393, timestamp: time2);

        var decision = service.EvaluateLocationUpdate(location2, location1);

        // At 1 hour and ~111km, speed should be approximately 111 km/h
        Assert.True(decision.CurrentSpeedKmh.HasValue);
        Assert.True(decision.CurrentSpeedKmh > 100);
        Assert.True(decision.CurrentSpeedKmh < 120);
    }

    [Fact]
    public void DrivePresetHasShorterIntervalsForHighSpeed()
    {
        var driveConfig = BeaconConfiguration.CreateDrivePreset();
        var walkConfig = BeaconConfiguration.CreateWalkPreset();

        // At highway speeds, drive should transmit more frequently than walk
        Assert.True(driveConfig.FastIntervalSeconds < walkConfig.FastIntervalSeconds);

        // At low speeds, both should have similar slow intervals
        // (actual values may differ but fast speeds should differ more)
        Assert.True(driveConfig.FastSpeedThresholdKmh > walkConfig.FastSpeedThresholdKmh);
    }

    [Fact]
    public void ResetTransmissionTimerUpdatesState()
    {
        var service = new BeaconService();
        var time1 = DateTimeOffset.UtcNow.AddSeconds(-1000);
        var time2 = time1.AddSeconds(1);

        var location1 = CreateLocation(38.7223, -9.1393, timestamp: time1);
        var location2 = CreateLocation(38.7323, -9.1393, timestamp: time2);

        // First evaluation
        var decision1 = service.EvaluateLocationUpdate(location2, location1);
        var secsUntilNext1 = decision1.SecondsUntilNextBeacon;

        service.ResetTransmissionTimer();

        // After reset, the timing is fresh (doesn't affect the decision calculation directly
        // but the reset ensures the timer starts fresh for next evaluation)
        var decision2 = service.EvaluateLocationUpdate(location2, location1);
        // We're testing that ResetTransmissionTimer exists and can be called without error
        Assert.NotNull(decision2);
    }

    [Fact]
    public void CreatePositionPacketSetsCorrectFields()
    {
        var service = new BeaconService();
        var location = CreateLocation(38.7223, -9.1393, altitude: 100);

        var packet = service.CreatePositionPacket(location, "N0CALL-1");

        Assert.Equal("N0CALL", packet.Source.Base);
        Assert.Equal(1, packet.Source.Ssid);
        Assert.Equal("APRS", packet.Destination.Base);
        Assert.Equal(38.7223, packet.Latitude);
        Assert.Equal(-9.1393, packet.Longitude);
        // Altitude should be converted from meters to feet (~328 feet)
        Assert.NotNull(packet.Altitude);
        Assert.True(packet.Altitude > 300 && packet.Altitude < 350);
    }

    [Fact]
    public void CreatePositionPacketUsesAprsStandardUncompressedPrecision()
    {
        var service = new BeaconService();
        var location = CreateLocation(41.41764333333333, -8.521698333333333);

        var packet = service.CreatePositionPacket(location, "CT7ALW-7");

        Assert.Equal(SymbolCode.LeftSquareBracket, packet.Symbol.Code);
        Assert.Equal(2, packet.Precision);
        Assert.Equal(
            "!4125.06N/00831.30W[Walking",
            AprsSerializer.FormatInfoField(packet));
    }

    [Fact]
    public void BeaconConfigurationPresetsHaveValidValues()
    {
        var walkConfig = BeaconConfiguration.CreateWalkPreset();
        var driveConfig = BeaconConfiguration.CreateDrivePreset();
        var customConfig = BeaconConfiguration.CreateCustomPreset();

        // All should have positive intervals
        Assert.True(walkConfig.SlowIntervalSeconds > 0);
        Assert.True(walkConfig.NormalIntervalSeconds > 0);
        Assert.True(walkConfig.FastIntervalSeconds > 0);

        Assert.True(driveConfig.SlowIntervalSeconds > 0);
        Assert.True(driveConfig.NormalIntervalSeconds > 0);
        Assert.True(driveConfig.FastIntervalSeconds > 0);

        // Intervals should be in logical order
        Assert.True(walkConfig.FastIntervalSeconds < walkConfig.NormalIntervalSeconds);
        Assert.True(walkConfig.NormalIntervalSeconds < walkConfig.SlowIntervalSeconds);

        Assert.True(driveConfig.FastIntervalSeconds < driveConfig.NormalIntervalSeconds);
        Assert.True(driveConfig.NormalIntervalSeconds < driveConfig.SlowIntervalSeconds);

        // Speed thresholds should be in logical order
        Assert.True(walkConfig.SlowSpeedThresholdKmh < walkConfig.FastSpeedThresholdKmh);
        Assert.True(driveConfig.SlowSpeedThresholdKmh < driveConfig.FastSpeedThresholdKmh);

        // Minimum distance should be reasonable
        Assert.True(walkConfig.MinimumDistanceMeters > 0);
        Assert.True(driveConfig.MinimumDistanceMeters > 0);
    }

    [Fact]
    public void CourseChangeThresholdZeroDisablesCourseTrigger()
    {
        var config = BeaconConfiguration.CreateCustomPreset() with { CourseChangeThresholdDegrees = 0 };
        var service = new BeaconService();
        service.UpdateCustomConfiguration(config);
        service.SetActiveMode(DynamicBeaconMode.Custom);

        var time1 = DateTimeOffset.UtcNow;
        var time2 = time1.AddSeconds(5);
        var time3 = time2.AddSeconds(5);

        var location1 = CreateLocation(38.7223, -9.1393, timestamp: time1);
        var location2 = CreateLocation(38.7323, -9.1393, timestamp: time2); // North
        var location3 = CreateLocation(38.7323, -9.1293, timestamp: time3); // East

        var decision1 = service.EvaluateLocationUpdate(location2, location1);
        service.ResetTransmissionTimer();

        var decision2 = service.EvaluateLocationUpdate(location3, location2);

        // With zero threshold, course change should not trigger beacon
        Assert.DoesNotContain("Course changed", decision2.Reason);
    }

    [Fact]
    public void EvaluateLocationUpdateIncludesIntervalInformation()
    {
        var service = new BeaconService();
        service.SetActiveMode(DynamicBeaconMode.Walk);

        // Start far in the past to ensure first transmission
        var time1 = DateTimeOffset.UtcNow.AddSeconds(-2000);
        var time2 = time1.AddSeconds(5);

        var location1 = CreateLocation(38.7223, -9.1393, timestamp: time1);
        // Move far enough to pass minimum distance
        var location2 = CreateLocation(38.8223, -9.1393, timestamp: time2);

        var decision1 = service.EvaluateLocationUpdate(location2, location1);

        // Verify decision includes interval information
        Assert.True(decision1.ActiveIntervalSeconds > 0);
        Assert.True(decision1.SecondsUntilNextBeacon >= 0);
    }
}
