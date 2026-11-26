// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using AetherAprs.Data.Entities;
using AetherAprs.Factories.ViewModels;
using AetherAprs.Models;
using AetherAprs.Models.Aprs;
using AetherAprs.Services.Location;
using AetherAprs.Services.Packets;
using AetherAprs.Services.Platform;
using AetherAprs.Services.UI;
using AetherAprs.ViewModels.Pages;
using Geo;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace AetherAprs.Tests.ViewModels.Pages;

public sealed class PacketDetailsViewModelTests : TestFixtureBase
{
    private static LocationData CreateLocation(double latitude, double longitude) =>
        new()
        {
            Location = new Coordinate(latitude, longitude),
            Accuracy = 10,
            Timestamp = DateTimeOffset.UtcNow
        };

    private static PacketRecord PositionPacket(double latitude, double longitude, long id = 1) =>
        new()
        {
            Id = id,
            SourceBase = "N0CALL",
            SourceSsid = AprsSsid.AdditionalStation1,
            PacketType = "Position",
            RawInfo = "!",
            Timestamp = DateTime.UtcNow,
            Position = new PositionDataRecord
            {
                Location = new Coordinate(latitude, longitude)
            }
        };

    private static PacketDetailsViewModel CreateViewModel(
        IPacketQueryService query,
        ILocationTrackingService locationTracking,
        ICompassService compass) =>
        new(
            query,
            Substitute.For<INavigationService>(),
            Substitute.For<IConversationViewModelFactory>(),
            locationTracking,
            compass,
            NullLogger<PacketDetailsViewModel>.Instance);

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 5000)
    {
        Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
        while (!condition() && sw.ElapsedMilliseconds < timeoutMs)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task Initialize_PartitionsPacketsIntoTabs()
    {
        IPacketQueryService query = Substitute.For<IPacketQueryService>();
        query.GetPacketsByCallsignAsync("N0CALL-1", 500, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<PacketRecord>>(
            [
                PositionPacket(38.5, -9.0, 1),
                PositionPacket(38.6, -9.1, 2),
                new PacketRecord
                {
                    Id = 3,
                    SourceBase = "N0CALL",
                    SourceSsid = AprsSsid.AdditionalStation1,
                    PacketType = "Weather",
                    Timestamp = DateTime.UtcNow,
                    Weather = new WeatherDataRecord { Temperature = 72.0 }
                },
                new PacketRecord
                {
                    Id = 4,
                    SourceBase = "N0CALL",
                    SourceSsid = AprsSsid.AdditionalStation1,
                    PacketType = "Status",
                    Timestamp = DateTime.UtcNow,
                    Status = new StatusDataRecord { Text = "On air" }
                }
            ]));

        ILocationTrackingService locationTracking = Substitute.For<ILocationTrackingService>();
        locationTracking.CurrentLocation.Returns((LocationData?)null);
        ICompassService compass = Substitute.For<ICompassService>();

        PacketDetailsViewModel vm = CreateViewModel(query, locationTracking, compass);
        vm.Initialize("N0CALL-1");
        await WaitUntilAsync(() => !vm.IsLoading);

        Assert.Equal(4, vm.Packets.Count);
        Assert.Equal(2, vm.PositionHistory.Count);
        Assert.Equal(2, vm.WeatherStatusTelemetry.Count);
        Assert.True(vm.HasPackets);
        Assert.True(vm.HasPositionHistory);
        Assert.True(vm.HasWeatherStatusTelemetry);
    }

    [Fact]
    public async Task Initialize_WithoutPackets_KeepsEveryTabInItsEmptyState()
    {
        IPacketQueryService query = Substitute.For<IPacketQueryService>();
        query.GetPacketsByCallsignAsync("N0CALL-1", 500, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<PacketRecord>>([]));

        ILocationTrackingService locationTracking = Substitute.For<ILocationTrackingService>();
        locationTracking.CurrentLocation.Returns((LocationData?)null);
        ICompassService compass = Substitute.For<ICompassService>();

        PacketDetailsViewModel vm = CreateViewModel(query, locationTracking, compass);
        Assert.False(vm.HasPackets);

        vm.Initialize("N0CALL-1");
        await WaitUntilAsync(() => !vm.IsLoading);

        Assert.Empty(vm.Packets);
        Assert.False(vm.HasPackets);
        Assert.False(vm.HasPositionHistory);
        Assert.False(vm.HasWeatherStatusTelemetry);
    }

    [Fact]
    public async Task DistanceAndBearing_ComputedFromUserLocation()
    {
        IPacketQueryService query = Substitute.For<IPacketQueryService>();
        query.GetPacketsByCallsignAsync("N0CALL-1", 500, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<PacketRecord>>(
                [PositionPacket(38.5, -9.01, 1)]));

        ILocationTrackingService locationTracking = Substitute.For<ILocationTrackingService>();
        locationTracking.CurrentLocation.Returns(CreateLocation(38.5, -9.0));
        ICompassService compass = Substitute.For<ICompassService>();
        compass.HeadingDegrees.Returns((double?)null);

        PacketDetailsViewModel vm = CreateViewModel(query, locationTracking, compass);
        vm.Initialize("N0CALL-1");
        await WaitUntilAsync(() => !vm.IsLoading);

        Assert.True(vm.HasDistance);
        Assert.NotEmpty(vm.DistanceText);
        Assert.NotEmpty(vm.BearingText);
        // Due west: bearing of ~270 degrees (clockwise from north).
        Assert.Equal(270.0, vm.RelativeBearingDegrees, 1.0);
    }

    [Fact]
    public async Task RelativeBearing_WithHeading_SubtractsCompassHeading()
    {
        IPacketQueryService query = Substitute.For<IPacketQueryService>();
        query.GetPacketsByCallsignAsync("N0CALL-1", 500, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<PacketRecord>>(
                [PositionPacket(38.5, -9.01, 1)]));

        ILocationTrackingService locationTracking = Substitute.For<ILocationTrackingService>();
        locationTracking.CurrentLocation.Returns(CreateLocation(38.5, -9.0));
        ICompassService compass = Substitute.For<ICompassService>();
        compass.HeadingDegrees.Returns((double?)45.0);

        PacketDetailsViewModel vm = CreateViewModel(query, locationTracking, compass);
        vm.Initialize("N0CALL-1");
        await WaitUntilAsync(() => !vm.IsLoading);

        // Station is due west (270°), phone heading is 45° → needle at 225°.
        Assert.Equal(225.0, vm.RelativeBearingDegrees, 1.0);
    }

    [Fact]
    public async Task NoDistance_WhenUserLocationUnavailable()
    {
        IPacketQueryService query = Substitute.For<IPacketQueryService>();
        query.GetPacketsByCallsignAsync("N0CALL-1", 500, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<PacketRecord>>(
                [PositionPacket(38.5, -9.01, 1)]));

        ILocationTrackingService locationTracking = Substitute.For<ILocationTrackingService>();
        locationTracking.CurrentLocation.Returns((LocationData?)null);
        ICompassService compass = Substitute.For<ICompassService>();

        PacketDetailsViewModel vm = CreateViewModel(query, locationTracking, compass);
        vm.Initialize("N0CALL-1");
        await WaitUntilAsync(() => !vm.IsLoading);

        Assert.False(vm.HasDistance);
        Assert.Empty(vm.DistanceText);
        Assert.Empty(vm.BearingText);
        Assert.Equal(0.0, vm.RelativeBearingDegrees);
    }

    [Fact]
    public async Task LocationUpdated_RecomputesDistance()
    {
        IPacketQueryService query = Substitute.For<IPacketQueryService>();
        query.GetPacketsByCallsignAsync("N0CALL-1", 500, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<PacketRecord>>(
                [PositionPacket(38.5, -9.01, 1)]));

        ILocationTrackingService locationTracking = Substitute.For<ILocationTrackingService>();
        locationTracking.CurrentLocation.Returns((LocationData?)null);
        ICompassService compass = Substitute.For<ICompassService>();

        PacketDetailsViewModel vm = CreateViewModel(query, locationTracking, compass);
        vm.Initialize("N0CALL-1");
        await WaitUntilAsync(() => !vm.IsLoading);

        Assert.False(vm.HasDistance);

        // User location becomes available after load.
        locationTracking.CurrentLocation.Returns(CreateLocation(38.5, -9.0));
        locationTracking.LocationUpdated += Raise.Event<EventHandler<LocationData>>(
            locationTracking, CreateLocation(38.5, -9.0));

        Assert.True(vm.HasDistance);
        Assert.NotEmpty(vm.DistanceText);
    }

    [Fact]
    public void OnRetiredFromOverlay_StopsCompassAndUnsubscribes()
    {
        IPacketQueryService query = Substitute.For<IPacketQueryService>();
        ILocationTrackingService locationTracking = Substitute.For<ILocationTrackingService>();
        locationTracking.CurrentLocation.Returns((LocationData?)null);
        ICompassService compass = Substitute.For<ICompassService>();

        PacketDetailsViewModel vm = CreateViewModel(query, locationTracking, compass);
        vm.Initialize("N0CALL-1");

        compass.Received(1).StartListening();

        vm.OnRetiredFromOverlay();

        compass.Received(1).StopListening();

        // Raising events after retirement must not throw (subscriptions removed).
        locationTracking.LocationUpdated += Raise.Event<EventHandler<LocationData>>(
            locationTracking, CreateLocation(38.5, -9.0));
        compass.HeadingUpdated += Raise.Event<EventHandler<double>>(compass, 45.0);
    }
}
