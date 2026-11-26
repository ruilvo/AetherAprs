// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AetherAprs.Configuration;
using AetherAprs.Configuration.Settings;
using AetherAprs.Data;
using AetherAprs.Data.Entities;
using AetherAprs.Data.Mappers;
using AetherAprs.Data.Packets;
using AetherAprs.Factories.Packets;
using AetherAprs.Models;
using AetherAprs.Models.Aprs;
using AetherAprs.Models.Aprs.Packets;
using AetherAprs.Services.Beaconing;
using AetherAprs.Services.Configuration;
using AetherAprs.Services.Contracts;
using AetherAprs.Services.Messaging;
using AetherAprs.Services.Packets;
using AetherAprs.Services.Platform;
using AetherAprs.Services.Ports;
using AetherAprs.Tests.Helpers;
using Geo;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace AetherAprs.Tests.Data;

public sealed class SavedDataPersistenceTests
{
    [Fact]
    public async Task BeaconServicePersistsActiveModeAndConfiguration()
    {
        using var db = TempAppDatabase.Create();
        var configuration = Substitute.For<IConfigurationService>();
        configuration.Current.Returns(new AppSettings
        {
            Aprs = new AprsSettings { Callsign = "N0CALL" }
        });
        
        var store = StoreFactory.CreateBeaconStateStore(db);
        await using var service = new BeaconService(store, Substitute.For<ILogger<BeaconService>>());
        await service.UpdateConfigurationAsync(
            BeaconConfig.CreateWalkPreset() with { SlowIntervalSeconds = 1111 },
            TestContext.Current.CancellationToken);
        await service.SetActiveModeAsync(DynamicBeaconMode.Drive, TestContext.Current.CancellationToken);

        // Writes are durable before the call returns, so a fresh service sees them.
        var reloaded = new BeaconService(
            StoreFactory.CreateBeaconStateStore(db),
            Substitute.For<ILogger<BeaconService>>());
        await reloaded.InitializeAsync(TestContext.Current.CancellationToken);
        Assert.Equal(DynamicBeaconMode.Drive, reloaded.CurrentConfiguration.Mode);
        Assert.Equal(1111, reloaded.AllConfigurations.Single(config => config.Mode == DynamicBeaconMode.Walk).SlowIntervalSeconds);
    }

    [Fact]
    public async Task BeaconService_FailedWrite_LeavesPreviousStateActive()
    {
        using var db = TempAppDatabase.Create();
        await using var service = new BeaconService(
            StoreFactory.CreateBeaconStateStore(db),
            Substitute.For<ILogger<BeaconService>>());
        await service.InitializeAsync(TestContext.Current.CancellationToken);

        var walk = service.AllConfigurations.Single(config => config.Mode == DynamicBeaconMode.Walk);

        // A destroyed schema makes the durable write fail.
        await using (var context = await db.Factory.CreateDbContextAsync(TestContext.Current.CancellationToken))
        {
            await context.Database.ExecuteSqlRawAsync("DROP TABLE BeaconConfigs", TestContext.Current.CancellationToken);
        }

        await Assert.ThrowsAnyAsync<Exception>(() => service.UpdateConfigurationAsync(
            walk with { SlowIntervalSeconds = 9999 }, TestContext.Current.CancellationToken));

        // Memory must not adopt a value that was never committed.
        Assert.Equal(
            walk.SlowIntervalSeconds,
            service.AllConfigurations.Single(config => config.Mode == DynamicBeaconMode.Walk).SlowIntervalSeconds);
    }

    [Fact]
    public async Task MessageServicePersistsHistoryAcrossRestart()
    {
        using var db = TempAppDatabase.Create();
        var portService = new FakePortService();
        var configuration = Substitute.For<IConfigurationService>();
        configuration.Current.Returns(new AppSettings
        {
            Aprs = new AprsSettings { Callsign = "N0CALL" }
        });

        var portId = Guid.NewGuid();
        var packet = new MessagePacket
        {
            Source = new Callsign("K0PEER", 1),
            Destination = new Callsign("APRS"),
            Addressee = AprsAddress.FromCallsign(new Callsign("N0CALL")),
            Text = "Ping",
            MessageNumber = 9
        };

        // Manually store the packet in the database (simulating what PacketStorageService would do)
        using (var context = db.CreateContext())
        {
            var record = PacketRecordMapper.ToRecord(packet, portId, DateTimeOffset.UtcNow, isOutbound: false);
            context.Packets.Add(record);
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        using (var service = new MessageService(
                   portService,
                   Substitute.For<IPacketStorageService>(),
                   configuration,
                   Substitute.For<IAprsPortSettingsResolver>(),
                   new TestPacketFactory(),
                   NullLogger<MessageService>.Instance,
                   SynchronousUiDispatcher.Instance,
                    new PacketQueryService(new SqliteRuntimeReader(db.Factory), NullLogger<PacketQueryService>.Instance)))
        {
            await service.InitializeAsync(TestContext.Current.CancellationToken);
            // Verify the service loaded the message from database
            var thread = Assert.Single(service.Conversations);
            Assert.Equal(AprsAddress.FromCallsign(new Callsign("K0PEER", 1)), thread.Peer);
            var stored = Assert.Single(thread.Messages);
            Assert.Equal("Ping", stored.Text);
            Assert.Equal(9, stored.MessageNumber);
        }

        // Reload and verify persistence
        using var reloaded = new MessageService(
            portService,
            Substitute.For<IPacketStorageService>(),
            configuration,
            Substitute.For<IAprsPortSettingsResolver>(),
            new TestPacketFactory(),
            NullLogger<MessageService>.Instance,
            SynchronousUiDispatcher.Instance,
            new PacketQueryService(new SqliteRuntimeReader(db.Factory), NullLogger<PacketQueryService>.Instance));
        await reloaded.InitializeAsync(TestContext.Current.CancellationToken);
        var threadReloaded = Assert.Single(reloaded.Conversations);
        Assert.Equal(AprsAddress.FromCallsign(new Callsign("K0PEER", 1)), threadReloaded.Peer);
        var storedReloaded = Assert.Single(threadReloaded.Messages);
        Assert.Equal("Ping", storedReloaded.Text);
        Assert.Equal(9, storedReloaded.MessageNumber);
    }

    private sealed class FakePortService : IPortService
    {
        public IReadOnlyList<PortConfig> Ports { get; } = [];
        public event EventHandler? PortsChanged
        {
            add { }
            remove { }
        }
        public event EventHandler<PortPacketReceivedEventArgs>? PacketReceived;

        public void RaisePacketReceived(PortPacketReceivedEventArgs args) =>
            PacketReceived?.Invoke(this, args);

        public Task SendPacketAsync(Guid id, AprsPacket packet) => Task.CompletedTask;
        public Task AddPortAsync(PortConfig port) => Task.CompletedTask;
        public Task UpdatePortAsync(PortConfig port) => Task.CompletedTask;
        public Task RemovePortAsync(Guid id) => Task.CompletedTask;
        public Task SetPortEnabledAsync(Guid id, bool enabled) => Task.CompletedTask;
        public Task SetPortShowOnMapAsync(Guid id, bool showOnMap) => Task.CompletedTask;
        public Task StopAllPortsAsync() => Task.CompletedTask;
        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class TestPacketFactory : IPacketFactory
    {
        public PositionPacket CreatePositionPacket(
            LocationData location,
            string callsign,
            SymbolTable symbolTable,
            SymbolCode symbolCode,
            double? course = null,
            string? comment = null)
        {
            var parts = callsign.Split('-');
            var source = parts.Length > 1
                ? new Callsign(parts[0], int.Parse(parts[1]))
                : new Callsign(parts[0]);

            return new PositionPacket
            {
                Source = source,
                Destination = new Callsign("APRS"),
                Location = location.Location,
                Symbol = new Symbol(symbolTable, symbolCode),
                Course = course,
                Comment = comment ?? string.Empty,
                Altitude = location.Altitude.HasValue ? (int)(location.Altitude.Value * 3.28084) : null
            };
        }

        public MessagePacket CreateMessagePacket(
            string sourceCallsign,
            AprsAddress addressee,
            string messageText,
            int? messageNumber = null)
        {
            var parts = sourceCallsign.Split('-');
            var source = parts.Length > 1
                ? new Callsign(parts[0], int.Parse(parts[1]))
                : new Callsign(parts[0]);

            return new MessagePacket
            {
                Source = source,
                Destination = new Callsign("APRS"),
                Addressee = addressee,
                Text = messageText,
                MessageNumber = messageNumber
            };
        }

        public MessageAckPacket CreateAckPacket(
            string sourceCallsign,
            AprsAddress addressee,
            int messageNumber)
        {
            var parts = sourceCallsign.Split('-');
            var source = parts.Length > 1
                ? new Callsign(parts[0], int.Parse(parts[1]))
                : new Callsign(parts[0]);

            return new MessageAckPacket
            {
                Source = source,
                Destination = new Callsign("APRS"),
                Addressee = addressee,
                MessageNumber = messageNumber
            };
        }

        public MessageRejPacket CreateRejPacket(
            string sourceCallsign,
            AprsAddress addressee,
            int messageNumber)
        {
            var parts = sourceCallsign.Split('-');
            var source = parts.Length > 1
                ? new Callsign(parts[0], int.Parse(parts[1]))
                : new Callsign(parts[0]);

            return new MessageRejPacket
            {
                Source = source,
                Destination = new Callsign("APRS"),
                Addressee = addressee,
                MessageNumber = messageNumber
            };
        }

        public ObjectPacket CreateObjectPacket(
            string sourceCallsign,
            string name,
            Coordinate location,
            SymbolTable symbolTable,
            SymbolCode symbolCode,
            bool isLive = true,
            string? comment = null)
        {
            var parts = sourceCallsign.Split('-');
            var source = parts.Length > 1
                ? new Callsign(parts[0], int.Parse(parts[1]))
                : new Callsign(parts[0]);

            return new ObjectPacket
            {
                Source = source,
                Destination = new Callsign("APRS"),
                Name = name,
                IsLive = isLive,
                Location = location,
                Symbol = new Symbol(symbolTable, symbolCode),
                Comment = comment
            };
        }

        public ItemPacket CreateItemPacket(
            string sourceCallsign,
            string name,
            Coordinate location,
            SymbolTable symbolTable,
            SymbolCode symbolCode,
            bool isLive = true,
            string? comment = null)
        {
            var parts = sourceCallsign.Split('-');
            var source = parts.Length > 1
                ? new Callsign(parts[0], int.Parse(parts[1]))
                : new Callsign(parts[0]);

            return new ItemPacket
            {
                Source = source,
                Destination = new Callsign("APRS"),
                Name = name,
                IsLive = isLive,
                Location = location,
                Symbol = new Symbol(symbolTable, symbolCode),
                Comment = comment
            };
        }

        public BulletinPacket CreateBulletinPacket(
            string sourceCallsign,
            string name,
            string text,
            int? messageNumber = null)
        {
            var parts = sourceCallsign.Split('-');
            var source = parts.Length > 1
                ? new Callsign(parts[0], int.Parse(parts[1]))
                : new Callsign(parts[0]);

            return new BulletinPacket
            {
                Source = source,
                Destination = new Callsign("APRS"),
                Name = name,
                Text = text,
                MessageNumber = messageNumber
            };
        }
    }
}
