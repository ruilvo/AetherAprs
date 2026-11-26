// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AetherAprs.Models;
using AetherAprs.Services.Location;
using AetherAprs.Tests.Helpers;
using Geo;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AetherAprs.Tests.Services.Location;

public sealed class LocationCoordinatorTests : TestFixtureBase
{
    private static LocationData CreateLocation(
        double latitude = 38.7223,
        double longitude = -9.1393,
        double accuracy = 12,
        DateTimeOffset? timestamp = null) => new()
        {
            Location = new Coordinate(latitude, longitude),
            Accuracy = accuracy,
            Timestamp = timestamp ?? DateTimeOffset.UtcNow
        };

    private static LocationCoordinator CreateCoordinator(
        FakeTrackingService tracking,
        TimeProvider? timeProvider = null) =>
        new(
            tracking,
            timeProvider ?? TimeProvider.System,
            NullLogger<LocationCoordinator>.Instance);

    [Fact]
    public async Task AcquireAsync_StartsOneWorkerForRepeatedPurposes()
    {
        FakeTrackingService tracking = new();
        using LocationCoordinator coordinator = CreateCoordinator(tracking);

        await coordinator.AcquireAsync(LocationPurpose.Map, cancellationToken: TestContext.Current.CancellationToken);
        await coordinator.AcquireAsync(LocationPurpose.Map, cancellationToken: TestContext.Current.CancellationToken);
        await coordinator.AcquireAsync(LocationPurpose.Beacon, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(1, tracking.StartCount);
        Assert.Equal(0, tracking.PermissionRequestCount);
        Assert.Equal(2, coordinator.ActivePurposes.Count);
    }

    [Fact]
    public async Task AcquireAsync_WithPermission_RequestsItBeforeStarting()
    {
        FakeTrackingService tracking = new();
        using LocationCoordinator coordinator = CreateCoordinator(tracking);

        await coordinator.AcquireAsync(LocationPurpose.Map, requestPermission: true, TestContext.Current.CancellationToken);

        Assert.Equal(1, tracking.PermissionRequestCount);
        Assert.Equal(1, tracking.StartCount);
    }

    [Fact]
    public async Task Release_KeepsTheWorkerWhileAnotherPurposeIsHeld()
    {
        FakeTrackingService tracking = new();
        using LocationCoordinator coordinator = CreateCoordinator(tracking);

        await coordinator.AcquireAsync(LocationPurpose.Map, cancellationToken: TestContext.Current.CancellationToken);
        await coordinator.AcquireAsync(LocationPurpose.Beacon, cancellationToken: TestContext.Current.CancellationToken);

        coordinator.Release(LocationPurpose.Beacon);
        Assert.Equal(0, tracking.StopCount);
        Assert.Equal(new[] { LocationPurpose.Map }, coordinator.ActivePurposes);

        coordinator.Release(LocationPurpose.Map);
        Assert.Equal(1, tracking.StopCount);
    }

    [Fact]
    public async Task Release_AnUnheldPurpose_DoesNotStopTheWorker()
    {
        FakeTrackingService tracking = new();
        using LocationCoordinator coordinator = CreateCoordinator(tracking);

        await coordinator.AcquireAsync(LocationPurpose.Map, cancellationToken: TestContext.Current.CancellationToken);
        coordinator.Release(LocationPurpose.Beacon);

        Assert.Equal(0, tracking.StopCount);
        Assert.Equal(1, tracking.StartCount);
    }

    [Fact]
    public async Task StopAll_ForgetsEveryPurposeAndStopsTheWorker()
    {
        FakeTrackingService tracking = new();
        using LocationCoordinator coordinator = CreateCoordinator(tracking);

        await coordinator.AcquireAsync(LocationPurpose.Map, cancellationToken: TestContext.Current.CancellationToken);
        await coordinator.AcquireAsync(LocationPurpose.Beacon, cancellationToken: TestContext.Current.CancellationToken);

        coordinator.StopAll();

        Assert.Empty(coordinator.ActivePurposes);
        Assert.Equal(1, tracking.StopCount);
    }

    [Fact]
    public async Task LocationFix_CarriesTheSampleTimeAndAccuracy()
    {
        FakeTrackingService tracking = new();
        using LocationCoordinator coordinator = CreateCoordinator(tracking);
        await coordinator.AcquireAsync(LocationPurpose.Map, cancellationToken: TestContext.Current.CancellationToken);

        DateTimeOffset sampledAt = new(2026, 11, 26, 10, 0, 0, TimeSpan.Zero);
        tracking.Raise(CreateLocation(accuracy: 25, timestamp: sampledAt));

        LocationSnapshot? snapshot = coordinator.Current;
        Assert.NotNull(snapshot);
        Assert.Equal(sampledAt, snapshot!.SampledAt);
        Assert.Equal(25, snapshot.AccuracyMeters);
    }

    [Fact]
    public async Task LocationFix_WithoutReportedAccuracy_LeavesAccuracyNull()
    {
        FakeTrackingService tracking = new();
        using LocationCoordinator coordinator = CreateCoordinator(tracking);
        await coordinator.AcquireAsync(LocationPurpose.Map, cancellationToken: TestContext.Current.CancellationToken);

        tracking.Raise(CreateLocation(accuracy: 0));

        Assert.NotNull(coordinator.Current);
        Assert.Null(coordinator.Current!.AccuracyMeters);
    }

    [Fact]
    public async Task TryGetFresh_UsesTheInjectedClock()
    {
        FakeTrackingService tracking = new();
        TestClock clock = new();
        using LocationCoordinator coordinator = CreateCoordinator(tracking, clock);
        await coordinator.AcquireAsync(LocationPurpose.Map, cancellationToken: TestContext.Current.CancellationToken);

        tracking.Raise(CreateLocation(timestamp: clock.UtcNow));

        Assert.True(coordinator.TryGetFresh(TimeSpan.FromMinutes(1), out LocationSnapshot? fresh));
        Assert.NotNull(fresh);

        clock.Advance(TimeSpan.FromMinutes(2));

        Assert.False(coordinator.TryGetFresh(TimeSpan.FromMinutes(1), out LocationSnapshot? stale));
        Assert.Null(stale);
    }

    [Fact]
    public async Task AcquireAfterStop_StartsANewWorker()
    {
        FakeTrackingService tracking = new();
        using LocationCoordinator coordinator = CreateCoordinator(tracking);

        await coordinator.AcquireAsync(LocationPurpose.Map, cancellationToken: TestContext.Current.CancellationToken);
        coordinator.Release(LocationPurpose.Map);
        await coordinator.AcquireAsync(LocationPurpose.Map, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(2, tracking.StartCount);
        Assert.Equal(1, tracking.StopCount);
    }

    [Fact]
    public async Task AcquireAsync_WhenTheWorkerCannotStart_ReleasesThePurpose()
    {
        FakeTrackingService tracking = new() { StartFailure = new InvalidOperationException("simulated") };
        using LocationCoordinator coordinator = CreateCoordinator(tracking);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => coordinator.AcquireAsync(LocationPurpose.Map, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Empty(coordinator.ActivePurposes);

        tracking.StartFailure = null;
        await coordinator.AcquireAsync(LocationPurpose.Map, cancellationToken: TestContext.Current.CancellationToken);

        // The failed attempt never started the worker, so the next acquire starts it once.
        Assert.Equal(1, tracking.StartCount);
        Assert.Single(coordinator.ActivePurposes);
    }

    [Fact]
    public void Dispose_StopsTheWorker()
    {
        FakeTrackingService tracking = new();
        LocationCoordinator coordinator = CreateCoordinator(tracking);

        coordinator.Dispose();
        coordinator.Dispose();

        Assert.Equal(1, tracking.StopCount);
    }

    private sealed class FakeTrackingService : ILocationTrackingService
    {
        private LocationData? _currentLocation;

        public LocationData? CurrentLocation => _currentLocation;
        public bool IsTracking => StartCount > 0;
        public bool IsLocationAvailable => true;
        public int StartCount { get; private set; }
        public int PermissionRequestCount { get; private set; }
        public int StopCount { get; private set; }
        public Exception? StartFailure { get; set; }

        public event EventHandler<LocationData>? LocationUpdated;

        public void Raise(LocationData location)
        {
            _currentLocation = location;
            LocationUpdated?.Invoke(this, location);
        }

        public Task StartTrackingAsync()
        {
            if (StartFailure is not null)
            {
                throw StartFailure;
            }

            StartCount++;
            return Task.CompletedTask;
        }

        public Task RequestPermissionAndStartTrackingAsync()
        {
            PermissionRequestCount++;
            StartCount++;
            return Task.CompletedTask;
        }

        public void StopTracking() => StopCount++;

        public void Dispose()
        {
        }
    }
}
