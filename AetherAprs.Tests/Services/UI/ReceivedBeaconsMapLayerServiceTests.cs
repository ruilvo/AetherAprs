// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using AetherAprs.Imaging;using AetherAprs.Models.Aprs;
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
