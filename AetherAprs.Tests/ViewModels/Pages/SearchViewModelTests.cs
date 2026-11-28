// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using AetherAprs.Data.Entities;
using AetherAprs.Factories.ViewModels;
using AetherAprs.Models.Aprs;
using AetherAprs.Services.Location;
using AetherAprs.Services.Packets;
using AetherAprs.Services.Platform;
using AetherAprs.Services.Ports;
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
        IPacketDetailsViewModelFactory? detailsFactory = null,
        IObjectDetailsViewModelFactory? objectDetailsFactory = null) =>
        new(
            query ?? Substitute.For<IPacketQueryService>(),
            navigation ?? Substitute.For<INavigationService>(),
            detailsFactory ?? Substitute.For<IPacketDetailsViewModelFactory>(),
            objectDetailsFactory ?? Substitute.For<IObjectDetailsViewModelFactory>(),
            NullLogger<SearchViewModel>.Instance);

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 5000)
    {
        Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
        while (!condition() && sw.ElapsedMilliseconds < timeoutMs)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task SearchQuery_PopulatesStationResults()
    {
        IPacketQueryService query = Substitute.For<IPacketQueryService>();
        query.SearchStationsAsync("N0CALL", 100, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<PacketRecord>>(
                [StationPacket("N0CALL")]));

        SearchViewModel vm = CreateViewModel(query: query);
        vm.SearchQuery = "N0CALL";
        await WaitUntilAsync(() => vm.Results.Count > 0);

        Assert.True(vm.HasSearched);
        Assert.False(vm.ShowNoResults);
        Assert.True(vm.HasResults);
        SearchResultItem result = Assert.Single(vm.Results);
        Assert.Equal("N0CALL-1", result.Name);
        Assert.Equal("Station", result.Kind);
        Assert.Equal("N0CALL-1", result.Source);
        Assert.Null(result.Subtitle);
    }

    [Fact]
    public async Task SearchQuery_ObjectResult_ShowsReportedBy()
    {
        IPacketQueryService query = Substitute.For<IPacketQueryService>();
        query.SearchStationsAsync("SHELTER", 100, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<PacketRecord>>(
                [ObjectPacket("SHELTER1")]));

        SearchViewModel vm = CreateViewModel(query: query);
        vm.SearchQuery = "SHELTER";
        await WaitUntilAsync(() => vm.Results.Count > 0);

        SearchResultItem result = Assert.Single(vm.Results);
        Assert.Equal("SHELTER1", result.Name);
        Assert.Equal("Object", result.Kind);
        Assert.Equal("N0CALL", result.Source);
        Assert.Equal("Reported by N0CALL", result.Subtitle);
        Assert.Equal("SHELTER1", result.ObjectName);
    }

    [Fact]
    public async Task SearchQuery_NoMatches_SetsShowNoResults()
    {
        IPacketQueryService query = Substitute.For<IPacketQueryService>();
        query.SearchStationsAsync("ZZZ", 100, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<PacketRecord>>([]));

        SearchViewModel vm = CreateViewModel(query: query);
        vm.SearchQuery = "ZZZ";
        await WaitUntilAsync(() => vm.HasSearched);

        Assert.Empty(vm.Results);
        Assert.True(vm.ShowNoResults);
        Assert.False(vm.HasResults);
    }

    [Fact]
    public void SearchQuery_Empty_ClearsResultsAndFlags()
    {
        IPacketQueryService query = Substitute.For<IPacketQueryService>();
        SearchViewModel vm = CreateViewModel(query: query);
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
        INavigationService navigation = Substitute.For<INavigationService>();
        SearchViewModel vm = CreateViewModel(navigation: navigation);

        vm.CloseCommand.Execute(null);

        navigation.Received(1).GoBack();
    }

    [Fact]
    public async Task StaleSearch_DoesNotReplaceNewerResultsOrHideItsSpinner()
    {
        IPacketQueryService query = Substitute.For<IPacketQueryService>();
        TaskCompletionSource<IReadOnlyList<PacketRecord>> oldResult = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<IReadOnlyList<PacketRecord>> newResult = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        query.SearchStationsAsync("OLD", 100, Arg.Any<CancellationToken>()).Returns(oldResult.Task);
        query.SearchStationsAsync("NEW", 100, Arg.Any<CancellationToken>()).Returns(newResult.Task);

        SearchViewModel vm = CreateViewModel(query: query);

        vm.SearchQuery = "OLD";
        Task oldSearch = vm.PendingSearch!;
        await WaitUntilAsync(() => HasQueryCall(query, "OLD"));

        vm.SearchQuery = "NEW";
        Task newSearch = vm.PendingSearch!;
        await WaitUntilAsync(() => HasQueryCall(query, "NEW"));
        Assert.True(vm.IsLoading);

        // The superseded search completes last, which is the case that used to
        // overwrite the results and cancel the newer spinner.
        oldResult.SetResult([StationPacket("OLD")]);
        await oldSearch;

        Assert.True(vm.IsLoading);
        Assert.Empty(vm.Results);

        newResult.SetResult([StationPacket("NEW")]);
        await newSearch;

        Assert.False(vm.IsLoading);
        SearchResultItem result = Assert.Single(vm.Results);
        Assert.Equal("NEW-1", result.Name);
    }

    [Fact]
    public async Task ClearingQueryWhileSearchInFlight_DiscardsTheResult()
    {
        IPacketQueryService query = Substitute.For<IPacketQueryService>();
        TaskCompletionSource<IReadOnlyList<PacketRecord>> inFlight = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        query.SearchStationsAsync("N0CALL", 100, Arg.Any<CancellationToken>()).Returns(inFlight.Task);

        SearchViewModel vm = CreateViewModel(query: query);

        vm.SearchQuery = "N0CALL";
        Task search = vm.PendingSearch!;
        await WaitUntilAsync(() => HasQueryCall(query, "N0CALL"));

        vm.SearchQuery = string.Empty;

        inFlight.SetResult([StationPacket("N0CALL")]);
        await search;

        Assert.Empty(vm.Results);
        Assert.False(vm.HasSearched);
        Assert.False(vm.IsLoading);
        Assert.Null(vm.PendingSearch);
    }

    private static bool HasQueryCall(IPacketQueryService query, string searchText) =>
        query.ReceivedCalls().Any(call =>
            call.GetMethodInfo().Name == nameof(IPacketQueryService.SearchStationsAsync)
            && call.GetArguments() is [string text, ..]
            && text == searchText);

    private static PacketDetailsViewModel CreatePacketDetailsViewModel() =>
        new(
            Substitute.For<IPacketQueryService>(),
            Substitute.For<INavigationService>(),
            Substitute.For<IConversationViewModelFactory>(),
            Substitute.For<ILocationCoordinator>(),
            Substitute.For<ICompassService>(),
            Substitute.For<IPortService>(),
            NullLogger<PacketDetailsViewModel>.Instance);

    [Fact]
    public void OpenResult_NavigatesToStationDetails()
    {
        IPacketDetailsViewModelFactory detailsFactory = Substitute.For<IPacketDetailsViewModelFactory>();
        PacketDetailsViewModel details = CreatePacketDetailsViewModel();
        detailsFactory.Create("N0CALL-1").Returns(details);
        INavigationService navigation = Substitute.For<INavigationService>();
        SearchViewModel vm = CreateViewModel(navigation: navigation, detailsFactory: detailsFactory);

        vm.OpenResultCommand.Execute(new SearchResultItem("N0CALL-1", "Station", "N0CALL-1", null));

        detailsFactory.Received(1).Create("N0CALL-1");
        navigation.Received(1).NavigateTo(details);
    }

    [Fact]
    public void OpenResult_Object_OpensObjectHistoryByIdentity()
    {
        IPacketDetailsViewModelFactory detailsFactory = Substitute.For<IPacketDetailsViewModelFactory>();
        IObjectDetailsViewModelFactory objectFactory = Substitute.For<IObjectDetailsViewModelFactory>();
        ObjectDetailsViewModel objectDetails = new(Substitute.For<IPacketQueryService>());
        objectFactory.Create("SHELTER1").Returns(objectDetails);
        INavigationService navigation = Substitute.For<INavigationService>();
        SearchViewModel vm = CreateViewModel(
            navigation: navigation,
            detailsFactory: detailsFactory,
            objectDetailsFactory: objectFactory);

        vm.OpenResultCommand.Execute(
            new SearchResultItem("SHELTER1", "Object", "N0CALL", "Reported by N0CALL", "SHELTER1"));

        objectFactory.Received(1).Create("SHELTER1");
        detailsFactory.DidNotReceive().Create(Arg.Any<string>());
        navigation.Received(1).NavigateTo(objectDetails);
    }

    [Fact]
    public void OpenResult_Null_DoesNothing()
    {
        IPacketDetailsViewModelFactory detailsFactory = Substitute.For<IPacketDetailsViewModelFactory>();
        INavigationService navigation = Substitute.For<INavigationService>();
        SearchViewModel vm = CreateViewModel(navigation: navigation, detailsFactory: detailsFactory);

        vm.OpenResultCommand.Execute(null);

        detailsFactory.DidNotReceive().Create(Arg.Any<string>());
        navigation.DidNotReceive().NavigateTo(Arg.Any<ViewModelBase>());
    }
}
