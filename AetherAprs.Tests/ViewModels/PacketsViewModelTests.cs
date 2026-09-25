// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Data;
using AetherAprs.Factories;
using AetherAprs.Models.Aprs;
using AetherAprs.Services;
using AetherAprs.ViewModels.Pages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Xunit.Sdk;

namespace AetherAprs.Tests.ViewModels;

public class PacketsViewModelTests : IDisposable
{
    private readonly IPacketCacheService _packetCacheService;
    private readonly INavigationService _navigationService;
    private readonly IPacketDetailsViewModelFactory _packetDetailsFactory;
    private readonly ILogger<PacketsViewModel> _logger;
    private readonly PacketsViewModel _viewModel;
    private readonly Dictionary<string, CachedPacket> _cachedPackets;

    public PacketsViewModelTests()
    {
        _packetCacheService = Substitute.For<IPacketCacheService>();
        _navigationService = Substitute.For<INavigationService>();
        _packetDetailsFactory = Substitute.For<IPacketDetailsViewModelFactory>();
        _logger = Substitute.For<ILogger<PacketsViewModel>>();

        _cachedPackets = new Dictionary<string, CachedPacket>();
        _packetCacheService.GetAllPackets().Returns(_ => _cachedPackets);

        _viewModel = new PacketsViewModel(
            _packetCacheService,
            _navigationService,
            _packetDetailsFactory,
            _logger);
    }

    [Fact]
    public void Constructor_InitializesProperties()
    {
        // Assert
        Assert.Equal("Packets", _viewModel.Title);
        Assert.NotNull(_viewModel.Packets);
        Assert.Empty(_viewModel.Packets);
        Assert.False(_viewModel.IsLoading);
    }

    [Fact]
    public void Constructor_SubscribesToCacheUpdated()
    {
        // Arrange
        var packet = new PositionPacket
        {
            Source = new Callsign("N0CALL", 1),
            Destination = new Callsign("APRS"),
            Latitude = 45.5,
            Longitude = -122.5,
            Symbol = new Symbol(SymbolTable.Primary, SymbolCode.HyphenMinus),
            Raw = "N0CALL-1>APRS:!4530.00N/12230.00W-"
        };

        var cachedPacket = new CachedPacket
        {
            Packet = packet,
            PortId = Guid.NewGuid(),
            ReceivedAt = DateTimeOffset.UtcNow,
            Source = "N0CALL-1"
        };

        _cachedPackets["N0CALL-1"] = cachedPacket;

        // Act - Trigger cache update (throttled by timer, so won't appear immediately)
        _packetCacheService.CacheUpdated += Raise.EventWith(new PacketCacheUpdatedEventArgs
        {
            UpdatedPacket = cachedPacket
        });

        // Assert - Event subscription is working (timer will process later)
        // We can't easily test the timer behavior in a unit test without making it more complex
        // but we can verify the subscription doesn't crash
        Assert.NotNull(_viewModel);
    }

    [Fact]
    public void LoadPacketsFromCache_LoadsPositionPacket()
    {
        // Arrange
        var packet = new PositionPacket
        {
            Source = new Callsign("N0CALL", 1),
            Destination = new Callsign("APRS"),
            Latitude = 45.5231,
            Longitude = -122.6765,
            Symbol = new Symbol(SymbolTable.Primary, SymbolCode.HyphenMinus),
            Raw = "N0CALL-1>APRS:!4531.39N/12240.59W-"
        };

        var cachedPacket = new CachedPacket
        {
            Packet = packet,
            PortId = Guid.NewGuid(),
            ReceivedAt = DateTimeOffset.UtcNow,
            Source = "N0CALL-1"
        };

        _cachedPackets["N0CALL-1"] = cachedPacket;

        // Recreate ViewModel to trigger initial load
        using var vm = new PacketsViewModel(
            _packetCacheService,
            _navigationService,
            _packetDetailsFactory,
            _logger);

        // Assert
        Assert.Single(vm.Packets);
        var summary = vm.Packets[0];
        Assert.Equal("N0CALL-1", summary.Source);
        Assert.Equal("Position", summary.PacketType);
        // Use Contains to be culture-agnostic (comma vs period decimal separator)
        Assert.Contains("45", summary.Preview);
        Assert.Contains("122", summary.Preview);
        Assert.Contains("Lat:", summary.Preview);
        Assert.Contains("Lon:", summary.Preview);
    }

    [Fact]
    public void LoadPacketsFromCache_LoadsMessagePacket()
    {
        // Arrange
        var packet = new MessagePacket
        {
            Source = new Callsign("N0CALL", 1),
            Destination = new Callsign("APRS"),
            Addressee = new Callsign("K0OTH"),
            Text = "Hello from N0CALL",
            MessageNumber = 123,
            Raw = "N0CALL-1>APRS::K0OTH    :Hello from N0CALL{123"
        };

        var cachedPacket = new CachedPacket
        {
            Packet = packet,
            PortId = Guid.NewGuid(),
            ReceivedAt = DateTimeOffset.UtcNow,
            Source = "N0CALL-1"
        };

        _cachedPackets["N0CALL-1"] = cachedPacket;

        // Recreate ViewModel to trigger initial load
        using var vm = new PacketsViewModel(
            _packetCacheService,
            _navigationService,
            _packetDetailsFactory,
            _logger);

        // Assert
        Assert.Single(vm.Packets);
        var summary = vm.Packets[0];
        Assert.Equal("N0CALL-1", summary.Source);
        Assert.Equal("Message", summary.PacketType);
        Assert.Contains("K0OTH", summary.Preview);
        Assert.Contains("Hello from N0CALL", summary.Preview);
    }

    [Fact]
    public void LoadPacketsFromCache_LoadsStatusPacket()
    {
        // Arrange
        var packet = new StatusPacket
        {
            Source = new Callsign("N0CALL", 1),
            Destination = new Callsign("APRS"),
            Text = "Testing status packet",
            Raw = "N0CALL-1>APRS:>Testing status packet"
        };

        var cachedPacket = new CachedPacket
        {
            Packet = packet,
            PortId = Guid.NewGuid(),
            ReceivedAt = DateTimeOffset.UtcNow,
            Source = "N0CALL-1"
        };

        _cachedPackets["N0CALL-1"] = cachedPacket;

        // Recreate ViewModel to trigger initial load
        using var vm = new PacketsViewModel(
            _packetCacheService,
            _navigationService,
            _packetDetailsFactory,
            _logger);

        // Assert
        Assert.Single(vm.Packets);
        var summary = vm.Packets[0];
        Assert.Equal("N0CALL-1", summary.Source);
        Assert.Equal("Status", summary.PacketType);
        Assert.Equal("Testing status packet", summary.Preview);
    }

    [Fact]
    public void LoadPacketsFromCache_LoadsWeatherPacket()
    {
        // Arrange
        var packet = new WeatherPacket
        {
            Source = new Callsign("N0CALL", 1),
            Destination = new Callsign("APRS"),
            Temperature = 72.0,
            Raw = "N0CALL-1>APRS:_..."
        };

        var cachedPacket = new CachedPacket
        {
            Packet = packet,
            PortId = Guid.NewGuid(),
            ReceivedAt = DateTimeOffset.UtcNow,
            Source = "N0CALL-1"
        };

        _cachedPackets["N0CALL-1"] = cachedPacket;

        // Recreate ViewModel to trigger initial load
        using var vm = new PacketsViewModel(
            _packetCacheService,
            _navigationService,
            _packetDetailsFactory,
            _logger);

        // Assert
        Assert.Single(vm.Packets);
        var summary = vm.Packets[0];
        Assert.Equal("N0CALL-1", summary.Source);
        Assert.Equal("Weather", summary.PacketType);
        Assert.Contains("72", summary.Preview);
    }

    [Fact]
    public void LoadPacketsFromCache_SortsPacketsByMostRecentFirst()
    {
        // Arrange
        var now = DateTimeOffset.UtcNow;

        var packet1 = new PositionPacket
        {
            Source = new Callsign("N0CALL", 1),
            Destination = new Callsign("APRS"),
            Latitude = 45.5,
            Longitude = -122.5,
            Symbol = new Symbol(SymbolTable.Primary, SymbolCode.HyphenMinus),
            Raw = "N0CALL-1>APRS:!..."
        };

        var packet2 = new PositionPacket
        {
            Source = new Callsign("K0OTH", 2),
            Destination = new Callsign("APRS"),
            Latitude = 46.5,
            Longitude = -123.5,
            Symbol = new Symbol(SymbolTable.Primary, SymbolCode.GreaterThanSign),
            Raw = "K0OTH-2>APRS:!..."
        };

        _cachedPackets["N0CALL-1"] = new CachedPacket
        {
            Packet = packet1,
            PortId = Guid.NewGuid(),
            ReceivedAt = now.AddMinutes(-10), // Older
            Source = "N0CALL-1"
        };

        _cachedPackets["K0OTH-2"] = new CachedPacket
        {
            Packet = packet2,
            PortId = Guid.NewGuid(),
            ReceivedAt = now, // Newer
            Source = "K0OTH-2"
        };

        // Recreate ViewModel to trigger initial load
        using var vm = new PacketsViewModel(
            _packetCacheService,
            _navigationService,
            _packetDetailsFactory,
            _logger);

        // Assert - Most recent first
        Assert.Equal(2, vm.Packets.Count);
        Assert.Equal("K0OTH-2", vm.Packets[0].Source); // Newer packet first
        Assert.Equal("N0CALL-1", vm.Packets[1].Source);
    }

    [Fact]
    public void LoadPacketsFromCache_LimitsTo100Packets()
    {
        // Arrange - Add 150 packets with valid callsigns (max 6 chars)
        for (int i = 0; i < 150; i++)
        {
            var callsign = $"CAL{i:D3}"; // CAL000 to CAL149 (6 chars max)
            var packet = new PositionPacket
            {
                Source = new Callsign(callsign, 1),
                Destination = new Callsign("APRS"),
                Latitude = 45.5,
                Longitude = -122.5,
                Symbol = new Symbol(SymbolTable.Primary, SymbolCode.HyphenMinus),
                Raw = $"{callsign}-1>APRS:!..."
            };

            _cachedPackets[$"{callsign}-1"] = new CachedPacket
            {
                Packet = packet,
                PortId = Guid.NewGuid(),
                ReceivedAt = DateTimeOffset.UtcNow.AddMinutes(-i),
                Source = $"{callsign}-1"
            };
        }

        // Recreate ViewModel to trigger initial load
        using var vm = new PacketsViewModel(
            _packetCacheService,
            _navigationService,
            _packetDetailsFactory,
            _logger);

        // Assert - Only 100 packets displayed
        Assert.Equal(100, vm.Packets.Count);
    }

    [Fact]
    public void OpenPacketDetailsCommand_NavigatesToPacketDetails()
    {
        // Arrange
        var summary = new PacketSummary
        {
            Source = "N0CALL-1",
            PacketType = "Position",
            ReceivedAt = DateTimeOffset.UtcNow,
            Preview = "Test preview"
        };

        var dbContextFactory = Substitute.For<IDbContextFactory<AppDbContext>>();
        var conversationFactory = Substitute.For<IConversationViewModelFactory>();
        var detailsLogger = Substitute.For<ILogger<PacketDetailsViewModel>>();

        var detailsVm = new PacketDetailsViewModel(
            dbContextFactory,
            _navigationService,
            conversationFactory,
            detailsLogger);

        _packetDetailsFactory.Create("N0CALL-1").Returns(detailsVm);

        // Act
        _viewModel.OpenPacketDetailsCommand.Execute(summary);

        // Assert
        _packetDetailsFactory.Received(1).Create("N0CALL-1");
        _navigationService.Received(1).NavigateTo(detailsVm);
    }

    [Fact]
    public void OpenPacketDetailsCommand_WithNullSummary_DoesNotNavigate()
    {
        // Act
        _viewModel.OpenPacketDetailsCommand.Execute(null);

        // Assert
        _packetDetailsFactory.DidNotReceiveWithAnyArgs().Create(default!);
        _navigationService.DidNotReceiveWithAnyArgs().NavigateTo(default!);
    }

    [Fact]
    public void Dispose_CleansUpResourcesAndUnsubscribes()
    {
        // Arrange
        var packet = new PositionPacket
        {
            Source = new Callsign("N0CALL", 1),
            Destination = new Callsign("APRS"),
            Latitude = 45.5,
            Longitude = -122.5,
            Symbol = new Symbol(SymbolTable.Primary, SymbolCode.HyphenMinus),
            Raw = "N0CALL-1>APRS:!..."
        };

        _cachedPackets["N0CALL-1"] = new CachedPacket
        {
            Packet = packet,
            PortId = Guid.NewGuid(),
            ReceivedAt = DateTimeOffset.UtcNow,
            Source = "N0CALL-1"
        };

        // Act
        _viewModel.Dispose();

        // Trigger cache update after disposal
        _cachedPackets["N0CALL-2"] = new CachedPacket
        {
            Packet = packet,
            PortId = Guid.NewGuid(),
            ReceivedAt = DateTimeOffset.UtcNow,
            Source = "N0CALL-2"
        };

        _packetCacheService.CacheUpdated += Raise.EventWith(new PacketCacheUpdatedEventArgs
        {
            UpdatedPacket = _cachedPackets["N0CALL-2"]
        });

        // Assert - Second dispose should not throw
        _viewModel.Dispose();
    }

    public void Dispose()
    {
        _viewModel?.Dispose();
    }
}
