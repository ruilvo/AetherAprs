// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Configuration;
using AetherAprs.Configuration.Settings;
using AetherAprs.Data;
using AetherAprs.Data.Entities;
using AetherAprs.Data.Mappers;
using AetherAprs.Models.Aprs;
using AetherAprs.Models.Aprs.Packets;
using AetherAprs.Models.Messaging;
using AetherAprs.Services.Configuration;
using AetherAprs.Services.Packets;
using AetherAprs.Tests.Helpers;
using Geo;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace AetherAprs.Tests.Services.Packets;

public sealed class PacketStorageServiceTests : IDisposable
{
    private readonly TempAppDatabase _db;
    private readonly IConfigurationService _configurationService;
    private readonly ILogger<PacketStorageService> _logger;
    private readonly PacketStorageService _service;

    public PacketStorageServiceTests()
    {
        _db = TempAppDatabase.CreateEmpty();
        using (var db = _db.CreateContext())
        {
            db.Database.EnsureCreated();
        }

        _configurationService = Substitute.For<IConfigurationService>();
        var aprsSettings = new AprsSettings { PacketRetentionDays = 30 };
        var appSettings = new AppSettings { Aprs = aprsSettings };
        _configurationService.Settings.Returns(appSettings);

        _logger = Substitute.For<ILogger<PacketStorageService>>();
        _service = new PacketStorageService(_db.Factory, _configurationService, _logger);
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    [Fact]
    public async Task StorePacketAsync_PositionPacket_SavesWithAllFields()
    {
        // Arrange
        var portId = Guid.NewGuid();
        var packet = new PositionPacket
        {
            Source = new Callsign("K0OTH", 1),
            Destination = new Callsign("APRS", 0),
            Location = new Coordinate(37.7749, -122.4194),
            Altitude = 150,
            Course = 90,
            Speed = 25,
            Symbol = new Symbol(SymbolTable.Primary, SymbolCode.HyphenMinus),
            Comment = "Test position",
            Raw = "K0OTH-1>APRS:=3746.49N/12225.16W-Test position",
            Timestamp = DateTimeOffset.UtcNow
        };

        // Act
        await _service.StorePacketAsync(packet, portId, isOutbound: false, TestContext.Current.CancellationToken);

        // Assert
        await using var db = await _db.Factory.CreateDbContextAsync(TestContext.Current.CancellationToken);
        var stored = await db.Packets
            .Include(p => p.Position)
            .FirstOrDefaultAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(stored);
        Assert.Equal("K0OTH", stored.SourceBase);
        Assert.Equal(AprsSsid.AdditionalStation1, stored.SourceSsid);
        Assert.Equal("APRS", stored.DestinationBase);
        Assert.Equal(AprsSsid.PrimaryStation, stored.DestinationSsid);
        Assert.Equal(PacketTypes.Position, stored.PacketType);
        Assert.Equal(portId, stored.PortId);
        Assert.False(stored.IsOutbound);

        Assert.NotNull(stored.Position);
        Assert.Equal(37.7749, stored.Position.Location.Latitude, precision: 4);
        Assert.Equal(-122.4194, stored.Position.Location.Longitude, precision: 4);
        Assert.Equal(150, stored.Position.Altitude);
        Assert.Equal(90, stored.Position.Course);
        Assert.Equal(25, stored.Position.Speed);
        Assert.Equal(SymbolTable.Primary, stored.Position.SymbolTable);
        Assert.Equal(SymbolCode.HyphenMinus, stored.Position.SymbolCode);
        Assert.Equal("Test position", stored.Position.Comment);
    }

    [Fact]
    public async Task StorePacketAsync_MessagePacket_SavesWithDeliveryStatus()
    {
        // Arrange
        var portId = Guid.NewGuid();
        var packet = new MessagePacket
        {
            Source = new Callsign("CT7ALW", 0),
            Destination = new Callsign("APRS", 0),
            Addressee = new Callsign("K0OTH", 1),
            Text = "Hello world",
            MessageNumber = 1,
            Raw = "CT7ALW>APRS::K0OTH-1  :Hello world{001"
        };

        // Act - store as outbound
        await _service.StorePacketAsync(packet, portId, isOutbound: true, TestContext.Current.CancellationToken);

        // Assert
        await using var db = await _db.Factory.CreateDbContextAsync(TestContext.Current.CancellationToken);
        var stored = await db.Packets
            .Include(p => p.Message)
            .FirstOrDefaultAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(stored);
        Assert.Equal(PacketTypes.Message, stored.PacketType);
        Assert.True(stored.IsOutbound);

        Assert.NotNull(stored.Message);
        Assert.Equal("K0OTH", stored.Message.AddresseeBase);
        Assert.Equal(AprsSsid.AdditionalStation1, stored.Message.AddresseeSsid);
        Assert.Equal("Hello world", stored.Message.Text);
        Assert.Equal(1, stored.Message.Number);
        Assert.Equal(MessageDeliveryStatus.Pending, stored.Message.DeliveryStatus);
        Assert.Equal(0, stored.Message.RetryCount);
        Assert.Null(stored.Message.NextRetryTime);
    }

    [Fact]
    public async Task StorePacketAsync_MessagePacketInbound_NoDeliveryStatus()
    {
        // Arrange
        var portId = Guid.NewGuid();
        var packet = new MessagePacket
        {
            Source = new Callsign("K0OTH", 1),
            Destination = new Callsign("APRS", 0),
            Addressee = new Callsign("CT7ALW", 0),
            Text = "Reply message",
            MessageNumber = 42,
            Raw = "K0OTH-1>APRS::CT7ALW   :Reply message{042"
        };

        // Act - store as inbound
        await _service.StorePacketAsync(packet, portId, isOutbound: false, TestContext.Current.CancellationToken);

        // Assert
        await using var db = await _db.Factory.CreateDbContextAsync(TestContext.Current.CancellationToken);
        var stored = await db.Packets
            .Include(p => p.Message)
            .FirstOrDefaultAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(stored);
        Assert.False(stored.IsOutbound);
        Assert.NotNull(stored.Message);
        Assert.Null(stored.Message.DeliveryStatus);
        Assert.Equal(0, stored.Message.RetryCount);
        Assert.Null(stored.Message.NextRetryTime);
    }

    [Fact]
    public async Task StorePacketAsync_StatusPacket_SavesCorrectly()
    {
        // Arrange
        var portId = Guid.NewGuid();
        var packet = new StatusPacket
        {
            Source = new Callsign("CT7ALW", 9),
            Destination = new Callsign("APRS", 0),
            Text = "Testing AetherAprs",
            Raw = "CT7ALW-9>APRS:>Testing AetherAprs"
        };

        // Act
        await _service.StorePacketAsync(packet, portId, isOutbound: false, TestContext.Current.CancellationToken);

        // Assert
        await using var db = await _db.Factory.CreateDbContextAsync(TestContext.Current.CancellationToken);
        var stored = await db.Packets
            .Include(p => p.Status)
            .FirstOrDefaultAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(stored);
        Assert.Equal(PacketTypes.Status, stored.PacketType);
        Assert.NotNull(stored.Status);
        Assert.Equal("Testing AetherAprs", stored.Status.Text);
    }

    [Fact]
    public async Task StorePacketAsync_WeatherPacket_SavesAllFields()
    {
        // Arrange
        var portId = Guid.NewGuid();
        var packet = new WeatherPacket
        {
            Source = new Callsign("WX0TST", 0),
            Destination = new Callsign("APWX", 0),
            Temperature = 22.5f,
            WindSpeed = 15,
            WindDirection = 270,
            Humidity = 65,
            Pressure = 1013.25f,
            Rain1h = 2.5f,
            Rain24h = 10.0f,
            Raw = "WX0TEST>APWX:_.../.../...t022...P001p010..."
        };

        // Act
        await _service.StorePacketAsync(packet, portId, isOutbound: false, TestContext.Current.CancellationToken);

        // Assert
        await using var db = await _db.Factory.CreateDbContextAsync(TestContext.Current.CancellationToken);
        var stored = await db.Packets
            .Include(p => p.Weather)
            .FirstOrDefaultAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(stored);
        Assert.Equal(PacketTypes.Weather, stored.PacketType);
        Assert.NotNull(stored.Weather);
        Assert.Equal(22.5f, stored.Weather.Temperature);
        Assert.Equal(15, stored.Weather.WindSpeed);
        Assert.Equal(270, stored.Weather.WindDirection);
        Assert.Equal(65, stored.Weather.Humidity);
        Assert.Equal(1013.25f, stored.Weather.Pressure);
        Assert.Equal(2.5f, stored.Weather.RainLastHour);
        Assert.Equal(10.0f, stored.Weather.RainLast24Hours);
    }

    [Fact]
    public async Task StorePacketAsync_UnknownPacket_SavesAsUnknown()
    {
        // Arrange
        var portId = Guid.NewGuid();
        var packet = new UnknownPacket
        {
            Source = new Callsign("TEST", 0),
            Destination = new Callsign("APRS", 0),
            Raw = "TEST>APRS:?Unknown format"
        };

        // Act
        await _service.StorePacketAsync(packet, portId, isOutbound: false, TestContext.Current.CancellationToken);

        // Assert
        await using var db = await _db.Factory.CreateDbContextAsync(TestContext.Current.CancellationToken);
        var stored = await db.Packets.FirstOrDefaultAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(stored);
        Assert.Equal(PacketTypes.Unknown, stored.PacketType);
        Assert.Equal("TEST", stored.SourceBase);
        Assert.Equal("TEST>APRS:?Unknown format", stored.RawInfo);
    }

    [Fact]
    public async Task StorePacketAsync_NullPortId_SavesSuccessfully()
    {
        // Arrange
        var packet = new PositionPacket
        {
            Source = new Callsign("K0OTH", 1),
            Destination = new Callsign("APRS", 0),
            Location = new Coordinate(37.7749, -122.4194),
            Symbol = new Symbol(SymbolTable.Primary, SymbolCode.HyphenMinus),
            Raw = "K0OTH-1>APRS:=3746.49N/12225.16W-"
        };

        // Act - null portId (synthetic packet or internal generation)
        await _service.StorePacketAsync(packet, portId: null, isOutbound: false, TestContext.Current.CancellationToken);

        // Assert
        await using var db = await _db.Factory.CreateDbContextAsync(TestContext.Current.CancellationToken);
        var stored = await db.Packets.FirstOrDefaultAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(stored);
        Assert.Null(stored.PortId);
    }

    [Fact]
    public async Task StorePacketAsync_RaisesPacketStoredEvent()
    {
        // Arrange
        var eventRaised = false;
        _service.PacketStored += (sender, e) => eventRaised = true;

        var packet = new PositionPacket
        {
            Source = new Callsign("K0OTH", 1),
            Destination = new Callsign("APRS", 0),
            Location = new Coordinate(37.7749, -122.4194),
            Symbol = new Symbol(SymbolTable.Primary, SymbolCode.HyphenMinus),
            Raw = "K0OTH-1>APRS:=3746.49N/12225.16W-"
        };

        // Act
        await _service.StorePacketAsync(packet, Guid.NewGuid(), isOutbound: false, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(eventRaised);
    }

    [Fact]
    public async Task StorePacketAsync_OnDatabaseError_ThrowsException()
    {
        // Arrange - use invalid path to force error
        var invalidPath = Path.Combine(Path.GetTempPath(), $"ReadOnlyDir-{Guid.NewGuid():N}", "invalid.db");
        var invalidFactory = new TestDbContextFactory(invalidPath);
        
        var service = new PacketStorageService(invalidFactory, _configurationService, _logger);
        
        var packet = new PositionPacket
        {
            Source = new Callsign("K0OTH", 1),
            Destination = new Callsign("APRS", 0),
            Location = new Coordinate(37.7749, -122.4194),
            Symbol = new Symbol(SymbolTable.Primary, SymbolCode.HyphenMinus),
            Raw = "K0OTH-1>APRS:=3746.49N/12225.16W-"
        };

        // Act & Assert - directory doesn't exist, can't create database
        await Assert.ThrowsAnyAsync<Exception>(() =>
            service.StorePacketAsync(packet, Guid.NewGuid(), isOutbound: false, TestContext.Current.CancellationToken));
    }

    private sealed class TestDbContextFactory(string path) : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => new(AppDbContext.CreateOptions(path));

        public Task<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }

    [Fact]
    public async Task CleanupOldPacketsAsync_RemovesPacketsOlderThanRetention()
    {
        // Arrange - insert 3 packets: 2 old, 1 recent
        await using (var db = await _db.Factory.CreateDbContextAsync(TestContext.Current.CancellationToken))
        {
            var oldPacket1 = new PacketRecord
            {
                SourceBase = "OLD1",
                SourceSsid = AprsSsid.PrimaryStation,
                DestinationBase = "APRS",
                DestinationSsid = AprsSsid.PrimaryStation,
                PacketType = PacketTypes.Unknown,
                Timestamp = DateTime.UtcNow.AddDays(-35), // Older than 30 days
                RawInfo = "OLD1>APRS:test"
            };

            var oldPacket2 = new PacketRecord
            {
                SourceBase = "OLD2",
                SourceSsid = AprsSsid.PrimaryStation,
                DestinationBase = "APRS",
                DestinationSsid = AprsSsid.PrimaryStation,
                PacketType = PacketTypes.Unknown,
                Timestamp = DateTime.UtcNow.AddDays(-45), // Older than 30 days
                RawInfo = "OLD2>APRS:test"
            };

            var recentPacket = new PacketRecord
            {
                SourceBase = "RECENT",
                SourceSsid = AprsSsid.PrimaryStation,
                DestinationBase = "APRS",
                DestinationSsid = AprsSsid.PrimaryStation,
                PacketType = PacketTypes.Unknown,
                Timestamp = DateTime.UtcNow.AddDays(-10), // Within 30 days
                RawInfo = "RECENT>APRS:test"
            };

            db.Packets.AddRange(oldPacket1, oldPacket2, recentPacket);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        // Act
        await _service.CleanupOldPacketsAsync(TestContext.Current.CancellationToken);

        // Assert
        await using (var db = await _db.Factory.CreateDbContextAsync(TestContext.Current.CancellationToken))
        {
            var remaining = await db.Packets.ToListAsync(TestContext.Current.CancellationToken);
            Assert.Single(remaining);
            Assert.Equal("RECENT", remaining[0].SourceBase);
        }
    }

    [Fact]
    public async Task CleanupOldPacketsAsync_NoOldPackets_DoesNothing()
    {
        // Arrange - insert only recent packets
        await using (var db = await _db.Factory.CreateDbContextAsync(TestContext.Current.CancellationToken))
        {
            var recentPacket = new PacketRecord
            {
                SourceBase = "RECENT",
                SourceSsid = AprsSsid.PrimaryStation,
                DestinationBase = "APRS",
                DestinationSsid = AprsSsid.PrimaryStation,
                PacketType = PacketTypes.Unknown,
                Timestamp = DateTime.UtcNow.AddDays(-5),
                RawInfo = "RECENT>APRS:test"
            };

            db.Packets.Add(recentPacket);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        // Act
        await _service.CleanupOldPacketsAsync(TestContext.Current.CancellationToken);

        // Assert
        await using (var db = await _db.Factory.CreateDbContextAsync(TestContext.Current.CancellationToken))
        {
            var remaining = await db.Packets.ToListAsync(TestContext.Current.CancellationToken);
            Assert.Single(remaining);
        }
    }

    [Fact]
    public async Task CleanupOldPacketsAsync_OnDatabaseError_DoesNotThrow()
    {
        // Arrange - use invalid path to force error
        var invalidPath = Path.Combine(Path.GetTempPath(), $"ReadOnlyDir-{Guid.NewGuid():N}", "invalid.db");
        var invalidFactory = new TestDbContextFactory(invalidPath);
        
        var service = new PacketStorageService(invalidFactory, _configurationService, _logger);

        // Act - should not throw, error is logged
        await service.CleanupOldPacketsAsync(TestContext.Current.CancellationToken);

        // Assert - no exception thrown, method returns normally
        Assert.True(true); // If we get here without exception, test passes
    }
}
