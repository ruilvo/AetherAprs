// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AetherAprs.Configuration;
using AetherAprs.Data;
using AetherAprs.Imaging;
using AetherAprs.Models.Aprs;
using AetherAprs.Services;
using AetherAprs.ViewModels;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace AetherAprs.Tests.ViewModels;

public sealed class ReceivedBeaconsViewModelTests : IDisposable
{
    private readonly IPortService _portService;
    private readonly IPacketQueryService _packetQueryService;
    private readonly IPacketStorageService _packetStorageService;
    private readonly IConfigurationService _configurationService;
    private readonly IAprsSymbolBitmapProvider _symbolBitmapProvider;
    private readonly ILogger<ReceivedBeaconsViewModel> _logger;
    private readonly ReceivedBeaconsViewModel _viewModel;
    private readonly List<PacketRecord> _storedPackets;

    public ReceivedBeaconsViewModelTests()
    {
        _portService = Substitute.For<IPortService>();
        _packetQueryService = Substitute.For<IPacketQueryService>();
        _packetStorageService = Substitute.For<IPacketStorageService>();
        _configurationService = Substitute.For<IConfigurationService>();
        _symbolBitmapProvider = Substitute.For<IAprsSymbolBitmapProvider>();
        _logger = Substitute.For<ILogger<ReceivedBeaconsViewModel>>();
        
        _portService.Ports.Returns(new List<PortConfig>());
        
        // Setup configuration service with default settings
        var appSettings = new AppSettings
        {
            Aprs = new AprsSettings
            {
                DisplayTimeRange = PacketDisplayTimeRange.LastDay,
                CustomDisplayTimeRangeHours = 12
            }
        };
        _configurationService.Settings.Returns(appSettings);
        
        // Use a shared list that tests can modify
        _storedPackets = new List<PacketRecord>();
        _packetQueryService.GetMostRecentPositionPacketsAsync(Arg.Any<int>())
            .Returns(callInfo => 
            {
                var dict = _storedPackets.ToDictionary(p => p.Source, p => p);
                return Task.FromResult<IReadOnlyDictionary<string, PacketRecord>>(dict);
            });
        _packetQueryService.GetPacketsByCallsignAsync(Arg.Any<string>(), Arg.Any<int>())
            .Returns(callInfo => Task.FromResult<IReadOnlyList<PacketRecord>>(new List<PacketRecord>()));
        
        // Configure mock to return a valid bitmap
        _symbolBitmapProvider.GetSymbolBitmap(Arg.Any<Symbol>()).Returns(callInfo => new SkiaSharp.SKBitmap(64, 64));
        
        _viewModel = new ReceivedBeaconsViewModel(
            _portService,
            _packetQueryService,
            _packetStorageService,
            _configurationService, 
            _symbolBitmapProvider, 
            _logger);
    }

    [Fact]
    public void Constructor_InitializesBeaconsLayer()
    {
        Assert.NotNull(_viewModel.BeaconsLayer);
        Assert.Equal("Received Beacons", _viewModel.BeaconsLayer.Name);
    }

    [Fact]
    public void Constructor_InitializesTrailsLayer()
    {
        Assert.NotNull(_viewModel.TrailsLayer);
        Assert.Equal("Position Trails", _viewModel.TrailsLayer.Name);
    }

    [Fact]
    public async Task OnPacketStored_PositionPacket_TriggersLayerRebuild()
    {
        // Arrange
        var portId = Guid.NewGuid();
        var port = new PortConfig
        {
            Id = portId,
            Name = "TestPort",
            ShowOnMap = true,
            TypeSettings = new AprsIsSettings()
        };
        _portService.Ports.Returns(new[] { port });

        var record = new PacketRecord
        {
            Id = 1,
            Source = "N0CALL-1",
            PacketType = "Position",
            Latitude = 45.5,
            Longitude = -122.5,
            SymbolTable = "/",
            SymbolCode = "-",
            ReceivedAt = DateTime.UtcNow,
            PortId = portId
        };

        // Act - Add to stored packets and raise event
        _storedPackets.Add(record);
        _packetStorageService.PacketStored += Raise.EventWith(EventArgs.Empty);

        // Give async operations time to complete
        await Task.Delay(100, TestContext.Current.CancellationToken);

        // Assert
        var layer = _viewModel.BeaconsLayer as Mapsui.Layers.WritableLayer;
        Assert.NotNull(layer);
        Assert.Single(layer.GetFeatures());
    }

    [Fact]
    public async Task OnPacketStored_ShowOnMapFalse_DoesNotDisplay()
    {
        // Arrange
        var portId = Guid.NewGuid();
        var port = new PortConfig
        {
            Id = portId,
            Name = "TestPort",
            ShowOnMap = false, // Hidden
            TypeSettings = new AprsIsSettings()
        };
        _portService.Ports.Returns(new[] { port });

        var record = new PacketRecord
        {
            Id = 1,
            Source = "N0CALL-1",
            PacketType = "Position",
            Latitude = 45.5,
            Longitude = -122.5,
            SymbolTable = "/",
            SymbolCode = "-",
            ReceivedAt = DateTime.UtcNow,
            PortId = portId
        };

        // Act - Add to stored packets and raise event
        _storedPackets.Add(record);
        _packetStorageService.PacketStored += Raise.EventWith(EventArgs.Empty);

        // Give async operations time to complete
        await Task.Delay(100, TestContext.Current.CancellationToken);

        // Assert - beacon is not displayed
        var layer = _viewModel.BeaconsLayer as Mapsui.Layers.WritableLayer;
        Assert.NotNull(layer);
        Assert.Empty(layer!.GetFeatures());
    }

    [Fact]
    public async Task PortsChanged_ShowOnMapToggled_RebuildsLayers()
    {
        // Arrange
        var portId = Guid.NewGuid();
        var port = new PortConfig
        {
            Id = portId,
            Name = "TestPort",
            ShowOnMap = false,
            TypeSettings = new AprsIsSettings()
        };
        _portService.Ports.Returns(new[] { port });

        var record = new PacketRecord
        {
            Id = 1,
            Source = "N0CALL-1",
            PacketType = "Position",
            Latitude = 45.5,
            Longitude = -122.5,
            SymbolTable = "/",
            SymbolCode = "-",
            ReceivedAt = DateTime.UtcNow,
            PortId = portId
        };

        _storedPackets.Add(record);

        // Act - Enable ShowOnMap and trigger PortsChanged
        port.ShowOnMap = true;
        _portService.PortsChanged += Raise.EventWith(EventArgs.Empty);

        // Give async operations time to complete
        await Task.Delay(100, TestContext.Current.CancellationToken);

        // Assert - beacon now appears
        var layer = _viewModel.BeaconsLayer as Mapsui.Layers.WritableLayer;
        Assert.NotNull(layer);
        Assert.Single(layer.GetFeatures());
    }

    [Fact]
    public async Task RebuildVisibleLayerAsync_MultipleBeacons_AddsAllVisible()
    {
        // Arrange
        var portId = Guid.NewGuid();
        var port = new PortConfig
        {
            Id = portId,
            Name = "TestPort",
            ShowOnMap = true,
            TypeSettings = new AprsIsSettings()
        };
        _portService.Ports.Returns(new[] { port });

        var records = new[]
        {
            new PacketRecord
            {
                Id = 1,
                Source = "N0CALL-1",
                PacketType = "Position",
                Latitude = 45.5,
                Longitude = -122.5,
                SymbolTable = "/",
                SymbolCode = "-",
                ReceivedAt = DateTime.UtcNow.AddMinutes(-5),
                PortId = portId
            },
            new PacketRecord
            {
                Id = 2,
                Source = "K0OTH-2",
                PacketType = "Position",
                Latitude = 46.5,
                Longitude = -123.5,
                SymbolTable = "\\",
                SymbolCode = ">",
                ReceivedAt = DateTime.UtcNow,
                PortId = portId
            }
        };

        _storedPackets.AddRange(records);

        // Act
        _portService.PortsChanged += Raise.EventWith(EventArgs.Empty);

        // Give async operations time to complete
        await Task.Delay(100, TestContext.Current.CancellationToken);

        // Assert
        var layer = _viewModel.BeaconsLayer as Mapsui.Layers.WritableLayer;
        Assert.NotNull(layer);
        Assert.Equal(2, layer.GetFeatures().Count());
    }

    [Fact]
    public void Dispose_CleansUpResourcesAndUnsubscribes()
    {
        // Act
        _viewModel.Dispose();

        // Trigger events after disposal
        _portService.PortsChanged += Raise.EventWith(EventArgs.Empty);
        
        var record = new PacketRecord
        {
            Id = 999,
            Source = "TEST",
            PacketType = "Position",
            Latitude = 45.0,
            Longitude = -122.0,
            ReceivedAt = DateTime.UtcNow,
            PortId = Guid.NewGuid()
        };
        _packetStorageService.PacketStored += Raise.EventWith(EventArgs.Empty);

        // Assert - Second dispose should not throw
        _viewModel.Dispose();
    }

    public void Dispose()
    {
        _viewModel?.Dispose();
    }
}
