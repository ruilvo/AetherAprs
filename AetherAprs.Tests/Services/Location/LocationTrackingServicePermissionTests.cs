// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Threading;
using System.Threading.Tasks;
using AetherAprs.Models;
using AetherAprs.Services.Location;
using AetherAprs.Services.Platform;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace AetherAprs.Tests.Services.Location;

public sealed class LocationTrackingServicePermissionTests
{
    [Fact]
    public async Task RequestPermissionAndStartTrackingAsync_WhenLocationIsDenied_ReportsAttentionAndDoesNotTrack()
    {
        ILocationService locationService = CreateAvailableLocationService();
        IPermissionService permissions = Substitute.For<IPermissionService>();
        permissions.RequestAsync(Arg.Any<PermissionKind>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(call.Arg<PermissionKind>() == PermissionKind.Location
                ? PermissionRequestResult.Denied(PermissionKind.Location)
                : PermissionRequestResult.Granted(call.Arg<PermissionKind>())));
        PermissionAttentionService attention = new();
        IKeepAliveService keepAlive = Substitute.For<IKeepAliveService>();
        using LocationTrackingService service = new(
            locationService,
            keepAlive,
            NullLogger<LocationTrackingService>.Instance,
            permissions,
            attention);

        await service.RequestPermissionAndStartTrackingAsync();

        Assert.False(service.IsTracking);
        await keepAlive.DidNotReceive().AcquireAsync(Arg.Any<KeepAliveReason>());

        PermissionAttention reported = Assert.Single(attention.Outstanding);
        Assert.Equal(PermissionKind.Location, reported.Kind);
        Assert.False(reported.RequiresApplicationSettings);
    }

    [Fact]
    public async Task RequestPermissionAndStartTrackingAsync_WhenALocationDenialIsPermanent_ReportsTheSettingsRecovery()
    {
        ILocationService locationService = CreateAvailableLocationService();
        IPermissionService permissions = Substitute.For<IPermissionService>();
        permissions.RequestAsync(Arg.Any<PermissionKind>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(call.Arg<PermissionKind>() == PermissionKind.Location
                ? PermissionRequestResult.PermanentlyDenied(PermissionKind.Location)
                : PermissionRequestResult.Granted(call.Arg<PermissionKind>())));
        PermissionAttentionService attention = new();
        IKeepAliveService keepAlive = Substitute.For<IKeepAliveService>();
        using LocationTrackingService service = new(
            locationService,
            keepAlive,
            NullLogger<LocationTrackingService>.Instance,
            permissions,
            attention);

        await service.RequestPermissionAndStartTrackingAsync();

        Assert.False(service.IsTracking);
        PermissionAttention reported = Assert.Single(attention.Outstanding);
        Assert.Equal(PermissionKind.Location, reported.Kind);
        Assert.True(reported.RequiresApplicationSettings);
    }

    [Fact]
    public async Task RequestPermissionAndStartTrackingAsync_WhenLocationIsGranted_ReportsAndStartsTracking()
    {
        ILocationService locationService = CreateAvailableLocationService();
        IPermissionService permissions = Substitute.For<IPermissionService>();
        permissions.RequestAsync(Arg.Any<PermissionKind>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(PermissionRequestResult.Granted(call.Arg<PermissionKind>())));
        PermissionAttentionService attention = new();
        IKeepAliveService keepAlive = Substitute.For<IKeepAliveService>();
        using LocationTrackingService service = new(
            locationService,
            keepAlive,
            NullLogger<LocationTrackingService>.Instance,
            permissions,
            attention);

        await service.RequestPermissionAndStartTrackingAsync();

        Assert.True(service.IsTracking);
        Assert.Empty(attention.Outstanding);
        await keepAlive.Received(1).AcquireAsync(KeepAliveReason.LocationTracking);

        service.StopTracking();
    }

    [Fact]
    public async Task RequestPermissionAndStartTrackingAsync_WithoutPermissionService_UsesTheLocationServiceDialog()
    {
        ILocationService locationService = CreateAvailableLocationService();
        locationService.RequestLocationPermissionAsync().Returns(Task.FromResult(false));
        IKeepAliveService keepAlive = Substitute.For<IKeepAliveService>();
        using LocationTrackingService service = new(
            locationService,
            keepAlive,
            NullLogger<LocationTrackingService>.Instance);

        await service.RequestPermissionAndStartTrackingAsync();

        Assert.False(service.IsTracking);
        await locationService.Received(1).RequestLocationPermissionAsync();
    }

    private static ILocationService CreateAvailableLocationService()
    {
        ILocationService locationService = Substitute.For<ILocationService>();
        locationService.IsLocationAvailable().Returns(true);
        locationService.GetCurrentLocationAsync(Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(new LocationData
            {
                Location = new Geo.Coordinate(37.7749, -122.4194),
                Timestamp = DateTimeOffset.UtcNow
            }));
        return locationService;
    }
}
