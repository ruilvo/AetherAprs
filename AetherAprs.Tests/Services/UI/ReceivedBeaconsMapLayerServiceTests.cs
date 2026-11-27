// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using AetherAprs.Imaging;using AetherAprs.Models.Aprs;
using AetherAprs.Data.Entities;
using AetherAprs.Services.Configuration;
using AetherAprs.Services.Packets;
using AetherAprs.Services.Ports;
using AetherAprs.Services.UI;
using AetherAprs.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SkiaSharp;
using Xunit;

namespace AetherAprs.Tests.Services.UI;

public sealed class ReceivedBeaconsMapLayerServiceTests : IDisposable
{
    private readonly IAprsSymbolBitmapProvider _bitmapProvider = Substitute.For<IAprsSymbolBitmapProvider>();
    private readonly AprsSymbolMapConverter _converter;
    private readonly IPacketQueryService _queryService = Substitute.For<IPacketQueryService>();
    private readonly IPacketStorageService _storageService = Substitute.For<IPacketStorageService>();
    private readonly IPacketViewFilterService _filterService = Substitute.For<IPacketViewFilterService>();

    public ReceivedBeaconsMapLayerServiceTests()
    {
        _bitmapProvider.GetSymbolBitmap(Arg.Any<Symbol>()).Returns(new SKBitmap(32, 32));
        _converter = new AprsSymbolMapConverter(_bitmapProvider);
    }

    [Fact]
    public void Dispose_StopsReactingToStoredPackets()
    {
        ReceivedBeaconsMapLayerService service = CreateService();

        service.Dispose();
        _storageService.PacketStored += Raise.Event();

        _queryService.DidNotReceiveWithAnyArgs()
            .GetMostRecentPositionPacketsAsync(default, TestContext.Current.CancellationToken);
    }

    [Fact]
    public void Dispose_LeavesTheSharedSymbolConverterUsable()
    {
        ReceivedBeaconsMapLayerService service = CreateService();

        service.Dispose();

        // The converter and its provider are application-lifetime singletons; a map
        // teardown must not poison the symbol pickers that share them.
        Assert.NotNull(_converter.CreateImageStyle(new Symbol(SymbolTable.Primary, SymbolCode.HyphenMinus), 0.25));
        _bitmapProvider.DidNotReceive().Dispose();
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        ReceivedBeaconsMapLayerService service = CreateService();

        service.Dispose();
        service.Dispose();
    }

    [Fact]
    public async Task NewerRebuild_CancelsTheSupersededRun()
    {
        // Arrange - the first query blocks until the test releases it, so a second
        // packet can arrive while the first rebuild is still in flight.
        TaskCompletionSource<IReadOnlyDictionary<string, PacketRecord>> firstQuery = new();
        CancellationToken observedToken = default;
        _filterService.GetVisiblePortIds().Returns([]);
        _queryService
            .GetMostRecentPositionPacketsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                observedToken = call.ArgAt<CancellationToken>(1);
                return firstQuery.Task;
            });

        ReceivedBeaconsMapLayerService service = CreateService();

        // Act - the first rebuild starts and parks inside the query.
        _storageService.PacketStored += Raise.Event();
        _storageService.PacketStored += Raise.Event();

        // Assert - the superseded run observes cancellation instead of finishing
        // against the stale dataset.
        await WaitUntilAsync(() => observedToken.IsCancellationRequested);

        Assert.True(observedToken.CanBeCanceled);
        Assert.True(observedToken.IsCancellationRequested);

        // Release the query so the loop can start the replacement; disposal must stay
        // a clean stop for the pending run.
        firstQuery.SetResult(new Dictionary<string, PacketRecord>());
        _queryService.GetMostRecentPositionPacketsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyDictionary<string, PacketRecord>>(
                new Dictionary<string, PacketRecord>()));
        service.Dispose();
    }

    [Fact]
    public async Task Dispose_CancelsAnInFlightRebuild()
    {
        // Arrange
        TaskCompletionSource<IReadOnlyDictionary<string, PacketRecord>> pending = new();
        CancellationToken observedToken = default;
        _filterService.GetVisiblePortIds().Returns([]);
        _queryService
            .GetMostRecentPositionPacketsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                observedToken = call.ArgAt<CancellationToken>(1);
                return pending.Task;
            });

        ReceivedBeaconsMapLayerService service = CreateService();
        _storageService.PacketStored += Raise.Event();

        // Act
        service.Dispose();

        // Assert - teardown stops the worker rather than letting it render into the
        // layers that were just cleared.
        Assert.True(observedToken.IsCancellationRequested);
        pending.SetResult(new Dictionary<string, PacketRecord>());
    }

    [Fact]
    public async Task PacketBurstDuringARebuild_QueuesExactlyOneReplacement()
    {
        // Arrange - the first query only completes when its token is cancelled, so the
        // latch has to coalesce a burst into one superseded run plus one replacement.
        int calls = 0;
        List<CancellationToken> tokens = [];
        _filterService.GetVisiblePortIds().Returns([]);
        _queryService
            .GetMostRecentPositionPacketsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                CancellationToken token = call.ArgAt<CancellationToken>(1);
                tokens.Add(token);
                calls++;
                return calls == 1
                    ? Task.Delay(Timeout.InfiniteTimeSpan, token)
                        .ContinueWith<IReadOnlyDictionary<string, PacketRecord>>(
                            _ => new Dictionary<string, PacketRecord>(),
                            TestContext.Current.CancellationToken,
                            TaskContinuationOptions.ExecuteSynchronously,
                            TaskScheduler.Default)
                    : Task.FromResult<IReadOnlyDictionary<string, PacketRecord>>(
                        new Dictionary<string, PacketRecord>());
            });

        ReceivedBeaconsMapLayerService service = CreateService();

        // Act
        _storageService.PacketStored += Raise.Event();
        for (int index = 0; index < 5; index++)
        {
            _storageService.PacketStored += Raise.Event();
        }

        // Assert - the five late events cancelled the first run and produced exactly
        // one more query, not five.
        await WaitUntilAsync(() => calls >= 2);
        await Task.Delay(100, TestContext.Current.CancellationToken);

        Assert.Equal(2, calls);
        Assert.True(tokens[0].IsCancellationRequested);
        Assert.All(tokens, token => Assert.True(token.CanBeCanceled));

        service.Dispose();
    }

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 5000)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        while (!condition() && stopwatch.ElapsedMilliseconds < timeoutMs)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }

    public void Dispose()
    {
        _converter.Dispose();
    }

    private ReceivedBeaconsMapLayerService CreateService()
    {
        _filterService.GetVisiblePortIds().Returns([]);
        return new ReceivedBeaconsMapLayerService(
            Substitute.For<IPortService>(),
            _queryService,
            _storageService,
            Substitute.For<IConfigurationService>(),
            _filterService,
            _converter,
            SynchronousUiDispatcher.Instance,
            NullLogger<ReceivedBeaconsMapLayerService>.Instance);
    }
}
