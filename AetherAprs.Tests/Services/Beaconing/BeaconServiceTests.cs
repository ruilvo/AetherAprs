// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AetherAprs.Models;
using AetherAprs.Models.Aprs;
using AetherAprs.Services.Beaconing;
using Geo;
using Xunit;

namespace AetherAprs.Tests.Services.Beaconing;

/// <summary>
/// Tests for the dynamic beacon service.
/// </summary>
public class BeaconServiceTests : TestFixtureBase
{
    private static LocationData CreateLocation(double latitude, double longitude, double? altitude = null, DateTimeOffset? timestamp = null) => new()
    {
        Location = new Coordinate(latitude, longitude),
        Altitude = altitude,
        Accuracy = 10,
        Timestamp = timestamp ?? DateTimeOffset.UtcNow
    };

    [Fact]
    public void ActiveModeStartsAsWalk()
    {
        BeaconService service = new();
        Assert.Equal(DynamicBeaconMode.Walk, service.CurrentConfiguration.Mode);
    }

    [Fact]
    public void AllConfigurationsIncludesThreeModes()
    {
        BeaconService service = new();
        IReadOnlyList<BeaconConfig> configs = service.AllConfigurations;

        Assert.Equal(3, configs.Count);
        Assert.Contains(configs, c => c.Mode == DynamicBeaconMode.Walk);
        Assert.Contains(configs, c => c.Mode == DynamicBeaconMode.Drive);
        Assert.Contains(configs, c => c.Mode == DynamicBeaconMode.Custom);
    }

    [Fact]
    public async Task SetActiveModeChangesCurrentConfiguration()
    {
        BeaconService service = new();

        await service.SetActiveModeAsync(DynamicBeaconMode.Drive, TestContext.Current.CancellationToken);
        Assert.Equal(DynamicBeaconMode.Drive, service.CurrentConfiguration.Mode);

        await service.SetActiveModeAsync(DynamicBeaconMode.Custom, TestContext.Current.CancellationToken);
        Assert.Equal(DynamicBeaconMode.Custom, service.CurrentConfiguration.Mode);

        await service.SetActiveModeAsync(DynamicBeaconMode.Walk, TestContext.Current.CancellationToken);
        Assert.Equal(DynamicBeaconMode.Walk, service.CurrentConfiguration.Mode);
    }

    [Fact]
    public async Task UpdateConfigurationModifiesCustomMode()
    {
        BeaconService service = new();
        BeaconConfig customConfig = BeaconConfig.CreateCustomPreset();
        BeaconConfig modifiedConfig = customConfig with { SlowIntervalSeconds = 3600 };

        await service.UpdateConfigurationAsync(modifiedConfig, TestContext.Current.CancellationToken);

        await service.SetActiveModeAsync(DynamicBeaconMode.Custom, TestContext.Current.CancellationToken);
        Assert.Equal(3600, service.CurrentConfiguration.SlowIntervalSeconds);
    }

    [Fact]
    public void ProcessLocationUpdateWithNoPreviousLocationUpdatesDecision()
    {
        BeaconService service = new();
        LocationData location = CreateLocation(38.7223, -9.1393);

        // First evaluation with no previous location
        service.ProcessLocationUpdate(location);
        BeaconTransmitDecision? decision = service.CurrentDecision;

        // Should return a valid decision
        Assert.NotNull(decision);
        Assert.True(decision.ActiveIntervalSeconds > 0);
    }

    [Fact]
    public void ProcessLocationUpdateIgnoresSmallDistances()
    {
        BeaconService service = new();
        DateTimeOffset time1 = DateTimeOffset.UtcNow.AddSeconds(-2000);
        DateTimeOffset time2 = time1.AddSeconds(5);

        // Two locations very close together (within 50m minimum)
        LocationData location1 = CreateLocation(38.7223, -9.1393, timestamp: time1);
        LocationData location2 = CreateLocation(38.72231, -9.13931, timestamp: time2); // ~11 meters apart

        service.ProcessLocationUpdate(location1);
        service.ProcessLocationUpdate(location2);
        BeaconTransmitDecision? decision = service.CurrentDecision;

        Assert.NotNull(decision);
        Assert.False(decision.ShouldTransmit);
        Assert.Contains("below minimum", decision.Reason.GetLocalizedString());
    }

    [Fact]
    public void ProcessLocationUpdateCalculatesCourseWhenHasPreviousLocation()
    {
        BeaconService service = new();

        DateTimeOffset time1 = DateTimeOffset.UtcNow.AddSeconds(-2000);
        DateTimeOffset time2 = time1.AddSeconds(10);

        // Start heading north
        LocationData location1 = CreateLocation(38.7223, -9.1393, timestamp: time1);
        // Move to a new location (north, same longitude)
        LocationData location2 = CreateLocation(38.7323, -9.1393, timestamp: time2);

        service.ProcessLocationUpdate(location1);
        service.ProcessLocationUpdate(location2);
        BeaconTransmitDecision? decision = service.CurrentDecision;

        // Should calculate course and speed
        Assert.NotNull(decision);
        Assert.NotNull(decision.CurrentCourseDegrees);
        Assert.NotNull(decision.CurrentSpeedKmh);
        // Course should be roughly north (0 or 360 degrees)
        Assert.True(decision.CurrentCourseDegrees < 45 || decision.CurrentCourseDegrees > 315);
    }

    [Fact]
    public void ProcessLocationUpdateCalculatesSpeedCorrectly()
    {
        BeaconService service = new();
        DateTimeOffset time1 = DateTimeOffset.UtcNow.AddSeconds(-5000);
        DateTimeOffset time2 = time1.AddSeconds(3600); // 1 hour later

        // Start at Lisbon
        LocationData location1 = CreateLocation(38.7223, -9.1393, timestamp: time1);
        // End at approximately 120km away (for rough estimate)
        // Moving approximately north at ~1 degree latitude = ~111km
        LocationData location2 = CreateLocation(39.7223, -9.1393, timestamp: time2);

        service.ProcessLocationUpdate(location1);
        service.ProcessLocationUpdate(location2);
        BeaconTransmitDecision? decision = service.CurrentDecision;

        // At 1 hour and ~111km, speed should be approximately 111 km/h
        Assert.NotNull(decision);
        Assert.True(decision.CurrentSpeedKmh.HasValue);
        Assert.True(decision.CurrentSpeedKmh > 100);
        Assert.True(decision.CurrentSpeedKmh < 120);
    }

    [Fact]
    public void DrivePresetHasShorterIntervalsForHighSpeed()
    {
        BeaconConfig driveConfig = BeaconConfig.CreateDrivePreset();
        BeaconConfig walkConfig = BeaconConfig.CreateWalkPreset();

        // At highway speeds, drive should transmit more frequently than walk
        Assert.True(driveConfig.FastIntervalSeconds < walkConfig.FastIntervalSeconds);

        // At low speeds, both should have similar slow intervals
        // (actual values may differ but fast speeds should differ more)
        Assert.True(driveConfig.FastSpeedThresholdKmh > walkConfig.FastSpeedThresholdKmh);
    }

    [Fact]
    public void BeaconConfigurationPresetsHaveValidValues()
    {
        BeaconConfig walkConfig = BeaconConfig.CreateWalkPreset();
        BeaconConfig driveConfig = BeaconConfig.CreateDrivePreset();
        _ = BeaconConfig.CreateCustomPreset();

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
    public async Task CourseChangeThresholdZeroDisablesCourseTrigger()
    {
        BeaconConfig config = BeaconConfig.CreateCustomPreset() with { CourseChangeThresholdDegrees = 0 };
        BeaconService service = new();
        await service.UpdateConfigurationAsync(config, TestContext.Current.CancellationToken);
        await service.SetActiveModeAsync(DynamicBeaconMode.Custom, TestContext.Current.CancellationToken);

        DateTimeOffset time1 = DateTimeOffset.UtcNow;
        DateTimeOffset time2 = time1.AddSeconds(5);
        DateTimeOffset time3 = time2.AddSeconds(5);

        LocationData location1 = CreateLocation(38.7223, -9.1393, timestamp: time1);
        LocationData location2 = CreateLocation(38.7323, -9.1393, timestamp: time2); // North
        LocationData location3 = CreateLocation(38.7323, -9.1293, timestamp: time3); // East

        service.ProcessLocationUpdate(location1);
        service.ProcessLocationUpdate(location2);
        service.ProcessLocationUpdate(location3);
        BeaconTransmitDecision? decision = service.CurrentDecision;

        // With zero threshold, course change should not trigger beacon
        Assert.NotNull(decision);
        Assert.DoesNotContain("Course changed", decision.Reason.GetLocalizedString());
    }

    [Fact]
    public async Task ProcessLocationUpdateIncludesIntervalInformation()
    {
        BeaconService service = new();
        await service.SetActiveModeAsync(DynamicBeaconMode.Walk, TestContext.Current.CancellationToken);

        // Start far in the past to ensure first transmission
        DateTimeOffset time1 = DateTimeOffset.UtcNow.AddSeconds(-2000);
        DateTimeOffset time2 = time1.AddSeconds(5);

        LocationData location1 = CreateLocation(38.7223, -9.1393, timestamp: time1);
        // Move far enough to pass minimum distance
        LocationData location2 = CreateLocation(38.8223, -9.1393, timestamp: time2);

        service.ProcessLocationUpdate(location1);
        service.ProcessLocationUpdate(location2);
        BeaconTransmitDecision? decision = service.CurrentDecision;

        // Verify decision includes interval information
        Assert.NotNull(decision);
        Assert.True(decision.ActiveIntervalSeconds > 0);
        Assert.True(decision.SecondsUntilNextBeacon >= 0);
    }

    [Fact]
    public void ProcessLocationUpdateAdvancesTimerOnTransmitDecision()
    {
        BeaconService service = new()
        {
            LastTransmitTime = DateTime.UtcNow.AddHours(-1)
        };

        LocationData location = CreateLocation(38.7223, -9.1393);
        service.ProcessLocationUpdate(location);
        BeaconTransmitDecision? decision1 = service.CurrentDecision;
        Assert.NotNull(decision1);
        Assert.True(decision1.ShouldTransmit);

        // After a transmit decision, timer should advance automatically
        service.ProcessLocationUpdate(location);
        BeaconTransmitDecision? decision2 = service.CurrentDecision;
        Assert.NotNull(decision2);
        Assert.False(decision2.ShouldTransmit);
    }

    [Fact]
    public void ReportBeaconTransmissionResult_Failure_SchedulesRetryAfterBackoff()
    {
        BeaconService service = new()
        {
            LastTransmitTime = DateTime.UtcNow.AddHours(-1)
        };

        LocationData location = CreateLocation(38.7223, -9.1393);
        service.ProcessLocationUpdate(location);
        BeaconTransmitDecision? decision = service.CurrentDecision;
        Assert.NotNull(decision);
        Assert.True(decision!.ShouldTransmit);
        int interval = decision.ActiveIntervalSeconds;
        Assert.True(interval > BeaconService.TransmissionRetryBackoffSeconds, "Test requires an interval larger than the backoff window.");

        // Report failure: the next evaluation must not re-request immediately, but must
        // come due after the backoff window rather than the full interval.
        service.ReportBeaconTransmissionResult(success: false);
        BeaconTransmitDecision? afterFailure = service.CurrentDecision;
        Assert.NotNull(afterFailure);
        Assert.False(afterFailure!.ShouldTransmit);
        Assert.InRange(afterFailure.SecondsUntilNextBeacon, 1, BeaconService.TransmissionRetryBackoffSeconds);
    }

    [Fact]
    public void ReportBeaconTransmissionResult_Success_KeepsNormalInterval()
    {
        BeaconService service = new()
        {
            LastTransmitTime = DateTime.UtcNow.AddHours(-1)
        };

        LocationData location = CreateLocation(38.7223, -9.1393);
        service.ProcessLocationUpdate(location);
        Assert.True(service.CurrentDecision!.ShouldTransmit);

        // Success keeps the timer reset at request time: no early retry.
        service.ReportBeaconTransmissionResult(success: true);
        BeaconTransmitDecision afterSuccess = service.CurrentDecision;
        Assert.NotNull(afterSuccess);
        Assert.False(afterSuccess!.ShouldTransmit);
    }

    [Fact]
    public async Task UpdateConfigurationPersistsWalkAndDriveModes()
    {
        BeaconService service = new();
        BeaconConfig walk = BeaconConfig.CreateWalkPreset() with { SlowIntervalSeconds = 1111 };
        BeaconConfig drive = BeaconConfig.CreateDrivePreset() with { FastIntervalSeconds = 33 };

        await service.UpdateConfigurationAsync(walk, TestContext.Current.CancellationToken);
        await service.UpdateConfigurationAsync(drive, TestContext.Current.CancellationToken);

        await service.SetActiveModeAsync(DynamicBeaconMode.Walk, TestContext.Current.CancellationToken);
        Assert.Equal(1111, service.CurrentConfiguration.SlowIntervalSeconds);

        await service.SetActiveModeAsync(DynamicBeaconMode.Drive, TestContext.Current.CancellationToken);
        Assert.Equal(33, service.CurrentConfiguration.FastIntervalSeconds);
    }

    [Fact]
    public async Task SetActiveMode_SameMode_DoesNotResetTimer()
    {
        BeaconService service = new();
        BeaconConfig custom = BeaconConfig.CreateCustomPreset() with
        {
            SlowIntervalSeconds = 60,
            NormalIntervalSeconds = 60,
            FastIntervalSeconds = 60,
            CourseChangeThresholdDegrees = 0,
            MinimumDistanceMeters = 1
        };
        await service.UpdateConfigurationAsync(custom, TestContext.Current.CancellationToken);
        await service.SetActiveModeAsync(DynamicBeaconMode.Custom, TestContext.Current.CancellationToken);

        DateTimeOffset time1 = DateTimeOffset.UtcNow.AddSeconds(-5);
        DateTimeOffset time2 = time1.AddSeconds(1);
        LocationData location1 = CreateLocation(38.7223, -9.1393, timestamp: time1);
        LocationData location2 = CreateLocation(38.7233, -9.1393, timestamp: time2);

        service.ProcessLocationUpdate(location1);
        service.ProcessLocationUpdate(location2);
        BeaconTransmitDecision? decision1 = service.CurrentDecision;
        Assert.NotNull(decision1);
        Assert.False(decision1.ShouldTransmit);

        await service.SetActiveModeAsync(DynamicBeaconMode.Custom, TestContext.Current.CancellationToken);
        service.ProcessLocationUpdate(location2);
        BeaconTransmitDecision? decision2 = service.CurrentDecision;
        Assert.NotNull(decision2);
        Assert.False(decision2.ShouldTransmit);
    }

}
