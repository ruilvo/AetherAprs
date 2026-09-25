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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace AetherAprs.Tests.ViewModels;

public sealed class ReceivedBeaconsViewModelTests : IDisposable
{
    private readonly IPortService _portService;
    private readonly IPacketCacheService _packetCacheService;
    private readonly IConfigurationService _configurationService;
    private readonly IAprsSymbolBitmapProvider _symbolBitmapProvider;
    private readonly ILogger<ReceivedBeaconsViewModel> _logger;
    private readonly ReceivedBeaconsViewModel _viewModel;
    private readonly Dictionary<string, CachedPacket> _cachedPackets;

    public ReceivedBeaconsViewModelTests()
    {
        _portService = Substitute.For<IPortService>();
        _packetCacheService = Substitute.For<IPacketCacheService>();
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
        
        // Use a shared dictionary that tests can modify
        _cachedPackets = new Dictionary<string, CachedPacket>();
        _packetCacheService.GetPositionPackets().Returns(_ => _cachedPackets);
        
        // Configure mock to return a valid bitmap
        _symbolBitmapProvider.GetSymbolBitmap(Arg.Any<Symbol>()).Returns(callInfo => new SkiaSharp.SKBitmap(64, 64));
        
        _viewModel = new ReceivedBeaconsViewModel(
            _portService, 
            _packetCacheService, 
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
    public void OnPacketReceived_PositionPacket_AddsToLayer()
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

        var packet = new PositionPacket
        {
            Source = new Callsign("N0CALL", 1),
            Destination = new Callsign("APRS"),
            Latitude = 45.5,
            Longitude = -122.5,
            Symbol = new Symbol(SymbolTable.Primary, SymbolCode.HyphenMinus)
        };

        var cachedPacket = new CachedPacket
        {
            Packet = packet,
            PortId = portId,
            ReceivedAt = DateTimeOffset.UtcNow,
            Source = "N0CALL-1"
        };

        // Act - Add to shared dictionary and raise event
        _cachedPackets["N0CALL-1"] = cachedPacket;
        _packetCacheService.CacheUpdated += Raise.EventWith(new PacketCacheUpdatedEventArgs
        {
            UpdatedPacket = cachedPacket
        });

        // Assert
        var layer = _viewModel.BeaconsLayer as Mapsui.Layers.WritableLayer;
        Assert.NotNull(layer);
        Assert.Single(layer.GetFeatures());
    }

    [Fact]
    public void OnPacketReceived_NonPositionPacket_DoesNotAddToLayer()
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

        var packet = new MessagePacket
        {
            Source = new Callsign("N0CALL", 1),
            Destination = new Callsign("APRS"),
            Addressee = new Callsign("K0OTH"),
            Text = "Hello"
        };

        // Act
        _portService.PacketReceived += Raise.EventWith(new PortPacketReceivedEventArgs
        {
            PortId = portId,
            Packet = packet
        });

        // Assert
        var layer = _viewModel.BeaconsLayer as Mapsui.Layers.WritableLayer;
        Assert.NotNull(layer);
        Assert.Empty(layer.GetFeatures());
    }

    [Fact]
    public void OnPacketReceived_ShowOnMapFalse_BuffersBeaconButDoesNotDisplay()
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

        var packet = new PositionPacket
        {
            Source = new Callsign("N0CALL", 1),
            Destination = new Callsign("APRS"),
            Latitude = 45.5,
            Longitude = -122.5,
            Symbol = new Symbol(SymbolTable.Primary, SymbolCode.HyphenMinus)
        };

        var cachedPacket = new CachedPacket
        {
            Packet = packet,
            PortId = portId,
            ReceivedAt = DateTimeOffset.UtcNow,
            Source = "N0CALL-1"
        };

        // Act - Add to shared dictionary and raise event
        _cachedPackets["N0CALL-1"] = cachedPacket;
        _packetCacheService.CacheUpdated += Raise.EventWith(new PacketCacheUpdatedEventArgs
        {
            UpdatedPacket = cachedPacket
        });

        // Assert - beacon is buffered but not displayed
        var layer = _viewModel.BeaconsLayer as Mapsui.Layers.WritableLayer;
        Assert.NotNull(layer);
        Assert.Empty(layer!.GetFeatures());

        // Now enable ShowOnMap and verify beacon appears
        port.ShowOnMap = true;
        _portService.PortsChanged += Raise.EventWith(EventArgs.Empty);

        Assert.Single(layer.GetFeatures());
    }

    [Fact]
    public void OnPacketReceived_UpdatesExistingCallsignBeacon()
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

        var packet1 = new PositionPacket
        {
            Source = new Callsign("N0CALL", 1),
            Destination = new Callsign("APRS"),
            Latitude = 45.5,
            Longitude = -122.5,
            Symbol = new Symbol(SymbolTable.Primary, SymbolCode.HyphenMinus)
        };

        var packet2 = new PositionPacket
        {
            Source = new Callsign("N0CALL", 1), // Same callsign
            Destination = new Callsign("APRS"),
            Latitude = 45.6, // Different location
            Longitude = -122.6,
            Symbol = new Symbol(SymbolTable.Primary, SymbolCode.HyphenMinus)
        };

        var cachedPacket1 = new CachedPacket
        {
            Packet = packet1,
            PortId = portId,
            ReceivedAt = DateTimeOffset.UtcNow,
            Source = "N0CALL-1"
        };

        var cachedPacket2 = new CachedPacket
        {
            Packet = packet2,
            PortId = portId,
            ReceivedAt = DateTimeOffset.UtcNow.AddSeconds(1),
            Source = "N0CALL-1"
        };

        // Act - First packet
        _cachedPackets["N0CALL-1"] = cachedPacket1;
        _packetCacheService.CacheUpdated += Raise.EventWith(new PacketCacheUpdatedEventArgs
        {
            UpdatedPacket = cachedPacket1
        });

        // Second packet updates the same callsign
        _cachedPackets["N0CALL-1"] = cachedPacket2;
        _packetCacheService.CacheUpdated += Raise.EventWith(new PacketCacheUpdatedEventArgs
        {
            UpdatedPacket = cachedPacket2
        });

        // Assert - only one beacon for the callsign
        var layer = _viewModel.BeaconsLayer as Mapsui.Layers.WritableLayer;
        Assert.NotNull(layer);
        Assert.Single(layer.GetFeatures());
    }

    [Fact]
    public void OnPortsChanged_RemovesBeaconsFromDeletedPorts()
    {
        // Arrange
        var port1Id = Guid.NewGuid();
        var port2Id = Guid.NewGuid();
        var port1 = new PortConfig
        {
            Id = port1Id,
            Name = "Port1",
            ShowOnMap = true,
            TypeSettings = new AprsIsSettings()
        };
        var port2 = new PortConfig
        {
            Id = port2Id,
            Name = "Port2",
            ShowOnMap = true,
            TypeSettings = new AprsIsSettings()
        };

        _portService.Ports.Returns(new[] { port1, port2 });

        var packet1 = new PositionPacket
        {
            Source = new Callsign("N0CALL", 1),
            Destination = new Callsign("APRS"),
            Latitude = 45.5,
            Longitude = -122.5,
            Symbol = new Symbol(SymbolTable.Primary, SymbolCode.HyphenMinus)
        };

        var packet2 = new PositionPacket
        {
            Source = new Callsign("K0OTH", 2),
            Destination = new Callsign("APRS"),
            Latitude = 46.5,
            Longitude = -123.5,
            Symbol = new Symbol(SymbolTable.Primary, SymbolCode.GreaterThanSign)
        };

        var cachedPacket1 = new CachedPacket
        {
            Packet = packet1,
            PortId = port1Id,
            ReceivedAt = DateTimeOffset.UtcNow,
            Source = "N0CALL-1"
        };

        var cachedPacket2 = new CachedPacket
        {
            Packet = packet2,
            PortId = port2Id,
            ReceivedAt = DateTimeOffset.UtcNow,
            Source = "K0OTH-2"
        };

        // Act - Add both packets
        _cachedPackets["N0CALL-1"] = cachedPacket1;
        _cachedPackets["K0OTH-2"] = cachedPacket2;

        _packetCacheService.CacheUpdated += Raise.EventWith(new PacketCacheUpdatedEventArgs
        {
            UpdatedPacket = cachedPacket1
        });

        _packetCacheService.CacheUpdated += Raise.EventWith(new PacketCacheUpdatedEventArgs
        {
            UpdatedPacket = cachedPacket2
        });

        var layer = _viewModel.BeaconsLayer as Mapsui.Layers.WritableLayer;
        Assert.NotNull(layer);
        Assert.Equal(2, layer!.GetFeatures().Count());

        // Act - remove port1, update cache and notify
        _portService.Ports.Returns(new[] { port2 });
        _cachedPackets.Remove("N0CALL-1");
        _portService.PortsChanged += Raise.EventWith(EventArgs.Empty);

        // Assert - only port2's beacon remains
        Assert.Single(layer.GetFeatures());
    }

    [Fact]
    public void Dispose_CleansUpResourcesAndUnsubscribes()
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

        var packet = new PositionPacket
        {
            Source = new Callsign("N0CALL", 1),
            Destination = new Callsign("APRS"),
            Latitude = 45.5,
            Longitude = -122.5,
            Symbol = new Symbol(SymbolTable.Primary, SymbolCode.HyphenMinus)
        };

        var cachedPacket = new CachedPacket
        {
            Packet = packet,
            PortId = portId,
            ReceivedAt = DateTimeOffset.UtcNow,
            Source = "N0CALL-1"
        };

        _cachedPackets["N0CALL-1"] = cachedPacket;

        _packetCacheService.CacheUpdated += Raise.EventWith(new PacketCacheUpdatedEventArgs
        {
            UpdatedPacket = cachedPacket
        });

        var layer = _viewModel.BeaconsLayer as Mapsui.Layers.WritableLayer;
        Assert.NotNull(layer);
        Assert.Single(layer!.GetFeatures());

        // Act
        _viewModel.Dispose();

        // Assert - layer is cleared
        Assert.Empty(layer.GetFeatures());

        // Verify no exception on second dispose
        _viewModel.Dispose();
    }

    [Fact]
    public void Dispose_IgnoresSubsequentPacketReceived()
    {
        // Arrange
        var layer = _viewModel.BeaconsLayer as Mapsui.Layers.WritableLayer;
        Assert.NotNull(layer);
        
        _viewModel.Dispose();

        var portId = Guid.NewGuid();
        var port = new PortConfig
        {
            Id = portId,
            Name = "TestPort",
            ShowOnMap = true,
            TypeSettings = new AprsIsSettings()
        };
        _portService.Ports.Returns(new[] { port });
        
        var packet = new PositionPacket
        {
            Source = new Callsign("N0CALL", 1),
            Destination = new Callsign("APRS"),
            Latitude = 45.5,
            Longitude = -122.5,
            Symbol = new Symbol(SymbolTable.Primary, SymbolCode.HyphenMinus)
        };

        var cachedPacket = new CachedPacket
        {
            Packet = packet,
            PortId = portId,
            ReceivedAt = DateTimeOffset.UtcNow,
            Source = "N0CALL-1"
        };

        _cachedPackets["N0CALL-1"] = cachedPacket;

        // Act - raise event after disposal
        // The event handler throws ObjectDisposedException, but NSubstitute doesn't propagate it
        // We verify the behavior by checking that no features are added
        _packetCacheService.CacheUpdated += Raise.EventWith(new PacketCacheUpdatedEventArgs
        {
            UpdatedPacket = cachedPacket
        });

        // Assert - no features should be added after disposal
        Assert.Empty(layer!.GetFeatures());
    }

    public void Dispose()
    {
        _viewModel.Dispose();
    }
}
