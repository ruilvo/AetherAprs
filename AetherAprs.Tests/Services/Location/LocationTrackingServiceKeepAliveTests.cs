// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Threading;
using System.Threading.Tasks;

using AetherAprs.Models;
using AetherAprs.Services.Location;
using AetherAprs.Services.Platform;

using Microsoft.Extensions.Logging.Abstractions;

using NSubstitute;

using Xunit;

namespace AetherAprs.Tests.Services.Location;

public sealed class LocationTrackingServiceKeepAliveTests
{
    [Fact]
    public async Task StartTrackingAsync_AcquiresLocationTrackingKeepAlive()
    {
        ILocationService locationService = CreateAvailableLocationService();
        IKeepAliveService keepAlive = Substitute.For<IKeepAliveService>();
        LocationTrackingService service = new(
            locationService,
            keepAlive,
            NullLogger<LocationTrackingService>.Instance);

        await service.StartTrackingAsync();

        await keepAlive.Received(1).AcquireAsync(KeepAliveReason.LocationTracking);

        service.StopTracking();
        service.Dispose();
    }

    [Fact]
    public async Task StopTracking_ReleasesLocationTrackingKeepAlive()
    {
        ILocationService locationService = CreateAvailableLocationService();
        IKeepAliveService keepAlive = Substitute.For<IKeepAliveService>();
        LocationTrackingService service = new(
            locationService,
            keepAlive,
            NullLogger<LocationTrackingService>.Instance);

        await service.StartTrackingAsync();

        service.StopTracking();

        // Release is fire-and-forget; wait for it to complete.
        await keepAlive.Received(1).ReleaseAsync(KeepAliveReason.LocationTracking);

        service.Dispose();
    }

    [Fact]
    public async Task StartTrackingAsync_LocationUnavailable_DoesNotAcquireKeepAlive()
    {
        ILocationService locationService = Substitute.For<ILocationService>();
        locationService.IsLocationAvailable().Returns(false);
        IKeepAliveService keepAlive = Substitute.For<IKeepAliveService>();
        LocationTrackingService service = new(
            locationService,
            keepAlive,
            NullLogger<LocationTrackingService>.Instance);

        await service.StartTrackingAsync();

        await keepAlive.DidNotReceive().AcquireAsync(Arg.Any<KeepAliveReason>());

        service.Dispose();
    }

    [Fact]
    public async Task StartTrackingAsync_AlreadyTracking_DoesNotAcquireTwice()
    {
        ILocationService locationService = CreateAvailableLocationService();
        IKeepAliveService keepAlive = Substitute.For<IKeepAliveService>();
        LocationTrackingService service = new(
            locationService,
            keepAlive,
            NullLogger<LocationTrackingService>.Instance);

        await service.StartTrackingAsync();
        await service.StartTrackingAsync();

        await keepAlive.Received(1).AcquireAsync(KeepAliveReason.LocationTracking);

        service.StopTracking();
        service.Dispose();
    }

    private static ILocationService CreateAvailableLocationService()
    {
        ILocationService locationService = Substitute.For<ILocationService>();
        locationService.IsLocationAvailable().Returns(true);
        locationService.GetCurrentLocationAsync(Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(new LocationData
            {
                Location = new Geo.Coordinate(37.7749, -122.4194),
                Timestamp = System.DateTimeOffset.UtcNow
            }));
        return locationService;
    }
}
