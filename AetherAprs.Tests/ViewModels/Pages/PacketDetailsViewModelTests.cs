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
using AetherAprs.Tests.Helpers;
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
        FakeLocationCoordinator locationTracking,
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
        query.GetPacketsByCallsignPageAsync("N0CALL-1", null, 50, Arg.Any<CancellationToken>())
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

        FakeLocationCoordinator locationTracking = new();
        locationTracking.Location = null;
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
        query.GetPacketsByCallsignPageAsync("N0CALL-1", null, 50, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<PacketRecord>>([]));

        FakeLocationCoordinator locationTracking = new();
        locationTracking.Location = null;
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
        query.GetPacketsByCallsignPageAsync("N0CALL-1", null, 50, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<PacketRecord>>(
                [PositionPacket(38.5, -9.01, 1)]));

        FakeLocationCoordinator locationTracking = new();
        locationTracking.SetLocation(CreateLocation(38.5, -9.0));
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
        query.GetPacketsByCallsignPageAsync("N0CALL-1", null, 50, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<PacketRecord>>(
                [PositionPacket(38.5, -9.01, 1)]));

        FakeLocationCoordinator locationTracking = new();
        locationTracking.SetLocation(CreateLocation(38.5, -9.0));
        ICompassService compass = Substitute.For<ICompassService>();
        compass.HeadingDegrees.Returns((double?)45.0);

        PacketDetailsViewModel vm = CreateViewModel(query, locationTracking, compass);
        vm.Initialize("N0CALL-1");
        await WaitUntilAsync(() => !vm.IsLoading);

        // Station is due west (270Ãƒâ€šÃ‚Â°), phone heading is 45Ãƒâ€šÃ‚Â° ÃƒÂ¢Ã¢â‚¬Â Ã¢â‚¬â„¢ needle at 225Ãƒâ€šÃ‚Â°.
        Assert.Equal(225.0, vm.RelativeBearingDegrees, 1.0);
    }

    [Fact]
    public async Task NoDistance_WhenUserLocationUnavailable()
    {
        IPacketQueryService query = Substitute.For<IPacketQueryService>();
        query.GetPacketsByCallsignPageAsync("N0CALL-1", null, 50, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<PacketRecord>>(
                [PositionPacket(38.5, -9.01, 1)]));

        FakeLocationCoordinator locationTracking = new();
        locationTracking.Location = null;
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
        query.GetPacketsByCallsignPageAsync("N0CALL-1", null, 50, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<PacketRecord>>(
                [PositionPacket(38.5, -9.01, 1)]));

        FakeLocationCoordinator locationTracking = new();
        locationTracking.Location = null;
        ICompassService compass = Substitute.For<ICompassService>();

        PacketDetailsViewModel vm = CreateViewModel(query, locationTracking, compass);
        vm.Initialize("N0CALL-1");
        await WaitUntilAsync(() => !vm.IsLoading);

        Assert.False(vm.HasDistance);

        // User location becomes available after load.
        locationTracking.SetLocation(CreateLocation(38.5, -9.0));
        locationTracking.SetLocation(CreateLocation(38.5, -9.0));

        Assert.True(vm.HasDistance);
        Assert.NotEmpty(vm.DistanceText);
    }

    [Fact]
    public void OnRetiredFromOverlay_StopsCompassAndUnsubscribes()
    {
        IPacketQueryService query = Substitute.For<IPacketQueryService>();
        FakeLocationCoordinator locationTracking = new();
        locationTracking.Location = null;
        ICompassService compass = Substitute.For<ICompassService>();

        PacketDetailsViewModel vm = CreateViewModel(query, locationTracking, compass);
        vm.Initialize("N0CALL-1");

        compass.Received(1).StartListening();

        vm.OnRetiredFromOverlay();

        compass.Received(1).StopListening();

        // Raising events after retirement must not throw (subscriptions removed).
        locationTracking.SetLocation(CreateLocation(38.5, -9.0));
        compass.HeadingUpdated += Raise.Event<EventHandler<double>>(compass, 45.0);
    }

    private static IReadOnlyList<PacketRecord> CreatePage(long newestId, int count)
    {
        List<PacketRecord> page = [];
        for (int index = 0; index < count; index++)
        {
            page.Add(PositionPacket(38.5, -9.0, newestId - index));
        }

        return page;
    }

    [Fact]
    public async Task Initialize_LoadsOnlyTheFirstPage()
    {
        IPacketQueryService query = Substitute.For<IPacketQueryService>();
        query.GetPacketsByCallsignPageAsync("N0CALL-1", null, 50, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(CreatePage(newestId: 100, count: 50)));

        FakeLocationCoordinator locationTracking = new();
        locationTracking.Location = null;
        ICompassService compass = Substitute.For<ICompassService>();

        PacketDetailsViewModel vm = CreateViewModel(query, locationTracking, compass);
        vm.Initialize("N0CALL-1");
        await WaitUntilAsync(() => !vm.IsLoading);

        // A full history is not materialized at once: the page holds one page of rows
        // and asks for the rest only when a list reaches its end.
        Assert.Equal(50, vm.Packets.Count);
        Assert.Equal(50, vm.PositionHistory.Count);
        Assert.True(vm.HasMore);
    }

    [Fact]
    public async Task LoadMore_AppendsTheNextPageFromTheOldestLoadedId()
    {
        IPacketQueryService query = Substitute.For<IPacketQueryService>();
        query.GetPacketsByCallsignPageAsync("N0CALL-1", null, 50, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(CreatePage(newestId: 100, count: 50)));
        query.GetPacketsByCallsignPageAsync("N0CALL-1", 51, 50, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(CreatePage(newestId: 50, count: 50)));
        query.GetPacketsByCallsignPageAsync("N0CALL-1", 1, 50, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<PacketRecord>>([]));

        FakeLocationCoordinator locationTracking = new();
        locationTracking.Location = null;
        ICompassService compass = Substitute.For<ICompassService>();

        PacketDetailsViewModel vm = CreateViewModel(query, locationTracking, compass);
        vm.Initialize("N0CALL-1");
        await WaitUntilAsync(() => !vm.IsLoading);

        await vm.LoadMoreCommand.ExecuteAsync(null);

        // The cursor is the oldest id already shown, so pages neither repeat nor skip
        // a row even when several packets share a timestamp.
        await query.Received(1).GetPacketsByCallsignPageAsync(
            "N0CALL-1", 51, 50, Arg.Any<CancellationToken>());
        Assert.Equal(100, vm.Packets.Count);
        Assert.Equal(100, vm.PositionHistory.Count);
        Assert.Equal(1, vm.Packets[^1].Id);
        Assert.True(vm.HasMore);

        await vm.LoadMoreCommand.ExecuteAsync(null);

        // A short page ends the history; the list keeps everything already shown.
        Assert.Equal(100, vm.Packets.Count);
        Assert.False(vm.HasMore);
    }

    [Fact]
    public async Task LoadMore_AfterAShortPage_DoesNotQueryAgain()
    {
        IPacketQueryService query = Substitute.For<IPacketQueryService>();
        query.GetPacketsByCallsignPageAsync("N0CALL-1", null, 50, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(CreatePage(newestId: 3, count: 3)));

        FakeLocationCoordinator locationTracking = new();
        locationTracking.Location = null;
        ICompassService compass = Substitute.For<ICompassService>();

        PacketDetailsViewModel vm = CreateViewModel(query, locationTracking, compass);
        vm.Initialize("N0CALL-1");
        await WaitUntilAsync(() => !vm.IsLoading);

        Assert.False(vm.HasMore);

        await vm.LoadMoreCommand.ExecuteAsync(null);

        await query.Received(1).GetPacketsByCallsignPageAsync(
            "N0CALL-1", Arg.Any<long?>(), 50, Arg.Any<CancellationToken>());
        Assert.Equal(3, vm.Packets.Count);
    }

    [Fact]
    public async Task RetiredOverlay_DoesNotAppendALatePage()
    {
        TaskCompletionSource<IReadOnlyList<PacketRecord>> pending = new();
        IPacketQueryService query = Substitute.For<IPacketQueryService>();
        query.GetPacketsByCallsignPageAsync("N0CALL-1", null, 50, Arg.Any<CancellationToken>())
            .Returns(pending.Task);

        FakeLocationCoordinator locationTracking = new();
        locationTracking.Location = null;
        ICompassService compass = Substitute.For<ICompassService>();

        PacketDetailsViewModel vm = CreateViewModel(query, locationTracking, compass);
        vm.Initialize("N0CALL-1");
        vm.OnRetiredFromOverlay();

        pending.SetResult(CreatePage(newestId: 5, count: 5));
        await WaitUntilAsync(() => !vm.IsLoading);

        Assert.Empty(vm.Packets);
        Assert.False(vm.HasPackets);
    }
}
