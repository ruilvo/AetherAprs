// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Linq;
using AetherAprs.Configuration;
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
    private readonly IAprsSymbolBitmapProvider _symbolBitmapProvider;
    private readonly ILogger<ReceivedBeaconsViewModel> _logger;
    private readonly ReceivedBeaconsViewModel _viewModel;

    public ReceivedBeaconsViewModelTests()
    {
        _portService = Substitute.For<IPortService>();
        _symbolBitmapProvider = Substitute.For<IAprsSymbolBitmapProvider>();
        _logger = Substitute.For<ILogger<ReceivedBeaconsViewModel>>();
        
        _portService.Ports.Returns(new List<PortConfig>());
        
        // Configure mock to return a valid bitmap
        _symbolBitmapProvider.GetSymbolBitmap(Arg.Any<Symbol>()).Returns(callInfo => new SkiaSharp.SKBitmap(64, 64));
        
        _viewModel = new ReceivedBeaconsViewModel(_portService, _symbolBitmapProvider, _logger);
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

        // Act
        _portService.PacketReceived += Raise.EventWith(new PortPacketReceivedEventArgs
        {
            PortId = portId,
            Packet = packet
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

        // Act
        _portService.PacketReceived += Raise.EventWith(new PortPacketReceivedEventArgs
        {
            PortId = portId,
            Packet = packet
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

        // Act
        _portService.PacketReceived += Raise.EventWith(new PortPacketReceivedEventArgs
        {
            PortId = portId,
            Packet = packet1
        });

        _portService.PacketReceived += Raise.EventWith(new PortPacketReceivedEventArgs
        {
            PortId = portId,
            Packet = packet2
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

        _portService.PacketReceived += Raise.EventWith(new PortPacketReceivedEventArgs
        {
            PortId = port1Id,
            Packet = packet1
        });

        _portService.PacketReceived += Raise.EventWith(new PortPacketReceivedEventArgs
        {
            PortId = port2Id,
            Packet = packet2
        });

        var layer = _viewModel.BeaconsLayer as Mapsui.Layers.WritableLayer;
        Assert.NotNull(layer);
        Assert.Equal(2, layer!.GetFeatures().Count());

        // Act - remove port1
        _portService.Ports.Returns(new[] { port2 });
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

        _portService.PacketReceived += Raise.EventWith(new PortPacketReceivedEventArgs
        {
            PortId = portId,
            Packet = packet
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

        // Act - raise event after disposal (should be ignored since handler is unsubscribed)
        _portService.PacketReceived += Raise.EventWith(new PortPacketReceivedEventArgs
        {
            PortId = portId,
            Packet = packet
        });

        // Assert - no features should be added (event was ignored)
        Assert.Empty(layer!.GetFeatures());
    }

    public void Dispose()
    {
        _viewModel.Dispose();
    }
}
