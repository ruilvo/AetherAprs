// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AetherAprs.Data.Entities;
using AetherAprs.Factories.ViewModels;
using AetherAprs.Models.Aprs;
using AetherAprs.Services.Location;
using AetherAprs.Services.Packets;
using AetherAprs.Services.Platform;
using AetherAprs.Services.UI;
using AetherAprs.ViewModels;
using AetherAprs.ViewModels.Pages;
using Geo;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace AetherAprs.Tests.ViewModels.Pages;

public sealed class SearchViewModelTests : TestFixtureBase
{
    private static PacketRecord StationPacket(string baseCallsign, AprsSsid ssid = AprsSsid.AdditionalStation1) =>
        new()
        {
            Id = 1,
            SourceBase = baseCallsign,
            SourceSsid = ssid,
            PacketType = "Position",
            Timestamp = DateTime.UtcNow
        };

    private static PacketRecord ObjectPacket(string objectName, string sourceBase = "N0CALL") =>
        new()
        {
            Id = 2,
            SourceBase = sourceBase,
            SourceSsid = AprsSsid.PrimaryStation,
            PacketType = "Object",
            Timestamp = DateTime.UtcNow,
            Object = new ObjectDataRecord
            {
                Name = objectName,
                IsLive = true,
                Location = new Coordinate(38.5, -9.0)
            }
        };

    private static SearchViewModel CreateViewModel(
        IPacketQueryService? query = null,
        INavigationService? navigation = null,
        IPacketDetailsViewModelFactory? detailsFactory = null) =>
        new(
            query ?? Substitute.For<IPacketQueryService>(),
            navigation ?? Substitute.For<INavigationService>(),
            detailsFactory ?? Substitute.For<IPacketDetailsViewModelFactory>(),
            NullLogger<SearchViewModel>.Instance);

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 5000)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (!condition() && sw.ElapsedMilliseconds < timeoutMs)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task SearchQuery_PopulatesStationResults()
    {
        var query = Substitute.For<IPacketQueryService>();
        query.SearchStationsAsync("N0CALL", 100, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<PacketRecord>>(
                [StationPacket("N0CALL")]));

        var vm = CreateViewModel(query: query);
        vm.SearchQuery = "N0CALL";
        await WaitUntilAsync(() => vm.Results.Count > 0);

        Assert.True(vm.HasSearched);
        Assert.False(vm.ShowNoResults);
        Assert.True(vm.HasResults);
        var result = Assert.Single(vm.Results);
        Assert.Equal("N0CALL-1", result.Name);
        Assert.Equal("Station", result.Kind);
        Assert.Equal("N0CALL-1", result.Source);
        Assert.Null(result.Subtitle);
    }

    [Fact]
    public async Task SearchQuery_ObjectResult_ShowsReportedBy()
    {
        var query = Substitute.For<IPacketQueryService>();
        query.SearchStationsAsync("SHELTER", 100, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<PacketRecord>>(
                [ObjectPacket("SHELTER1")]));

        var vm = CreateViewModel(query: query);
        vm.SearchQuery = "SHELTER";
        await WaitUntilAsync(() => vm.Results.Count > 0);

        var result = Assert.Single(vm.Results);
        Assert.Equal("SHELTER1", result.Name);
        Assert.Equal("Object", result.Kind);
        Assert.Equal("N0CALL", result.Source);
        Assert.Equal("Reported by N0CALL", result.Subtitle);
    }

    [Fact]
    public async Task SearchQuery_NoMatches_SetsShowNoResults()
    {
        var query = Substitute.For<IPacketQueryService>();
        query.SearchStationsAsync("ZZZ", 100, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<PacketRecord>>([]));

        var vm = CreateViewModel(query: query);
        vm.SearchQuery = "ZZZ";
        await WaitUntilAsync(() => vm.HasSearched);

        Assert.Empty(vm.Results);
        Assert.True(vm.ShowNoResults);
        Assert.False(vm.HasResults);
    }

    [Fact]
    public void SearchQuery_Empty_ClearsResultsAndFlags()
    {
        var query = Substitute.For<IPacketQueryService>();
        var vm = CreateViewModel(query: query);
        vm.Results.Add(new SearchResultItem("N0CALL", "Station", "N0CALL", null));
        vm.HasSearched = true;
        vm.ShowNoResults = true;

        vm.SearchQuery = "   ";

        Assert.Empty(vm.Results);
        Assert.False(vm.HasSearched);
        Assert.False(vm.ShowNoResults);
        Assert.False(vm.HasResults);
    }

    [Fact]
    public void Close_CallsNavigationGoBack()
    {
        var navigation = Substitute.For<INavigationService>();
        var vm = CreateViewModel(navigation: navigation);

        vm.CloseCommand.Execute(null);

        navigation.Received(1).GoBack();
    }

    private static PacketDetailsViewModel CreatePacketDetailsViewModel() =>
        new(
            Substitute.For<IPacketQueryService>(),
            Substitute.For<INavigationService>(),
            Substitute.For<IConversationViewModelFactory>(),
            Substitute.For<ILocationTrackingService>(),
            Substitute.For<ICompassService>(),
            NullLogger<PacketDetailsViewModel>.Instance);

    [Fact]
    public void OpenResult_NavigatesToStationDetails()
    {
        var detailsFactory = Substitute.For<IPacketDetailsViewModelFactory>();
        var details = CreatePacketDetailsViewModel();
        detailsFactory.Create("N0CALL-1").Returns(details);
        var navigation = Substitute.For<INavigationService>();
        var vm = CreateViewModel(navigation: navigation, detailsFactory: detailsFactory);

        vm.OpenResultCommand.Execute(new SearchResultItem("N0CALL-1", "Station", "N0CALL-1", null));

        detailsFactory.Received(1).Create("N0CALL-1");
        navigation.Received(1).NavigateTo(details);
    }

    [Fact]
    public void OpenResult_Object_OpensReportingStationDetails()
    {
        var detailsFactory = Substitute.For<IPacketDetailsViewModelFactory>();
        var details = CreatePacketDetailsViewModel();
        detailsFactory.Create("N0CALL").Returns(details);
        var navigation = Substitute.For<INavigationService>();
        var vm = CreateViewModel(navigation: navigation, detailsFactory: detailsFactory);

        vm.OpenResultCommand.Execute(new SearchResultItem("SHELTER1", "Object", "N0CALL", "Reported by N0CALL"));

        detailsFactory.Received(1).Create("N0CALL");
        navigation.Received(1).NavigateTo(details);
    }

    [Fact]
    public void OpenResult_Null_DoesNothing()
    {
        var detailsFactory = Substitute.For<IPacketDetailsViewModelFactory>();
        var navigation = Substitute.For<INavigationService>();
        var vm = CreateViewModel(navigation: navigation, detailsFactory: detailsFactory);

        vm.OpenResultCommand.Execute(null);

        detailsFactory.DidNotReceive().Create(Arg.Any<string>());
        navigation.DidNotReceive().NavigateTo(Arg.Any<ViewModelBase>());
    }
}
