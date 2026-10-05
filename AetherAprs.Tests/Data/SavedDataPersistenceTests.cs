// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AetherAprs.Configuration;
using AetherAprs.Configuration.Settings;
using AetherAprs.Data;
using AetherAprs.Data.Mappers;
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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace AetherAprs.Tests.Data;

public sealed class SavedDataPersistenceTests
{
    [Fact]
    public void BeaconServicePersistsActiveModeAndConfiguration()
    {
        using var db = TempAppDatabase.Create();
        var configuration = Substitute.For<IConfigurationService>();
        configuration.Settings.Returns(new AppSettings
        {
            Aprs = new AprsSettings { Callsign = "N0CALL" }
        });
        
        var service = new BeaconService(db.Factory, Substitute.For<ILogger<BeaconService>>());
        service.UpdateConfiguration(BeaconConfig.CreateWalkPreset() with { SlowIntervalSeconds = 1111 });
        service.SetActiveMode(DynamicBeaconMode.Drive);

        var reloaded = new BeaconService(db.Factory, Substitute.For<ILogger<BeaconService>>());
        Assert.Equal(DynamicBeaconMode.Drive, reloaded.CurrentConfiguration.Mode);
        Assert.Equal(1111, reloaded.AllConfigurations.Single(config => config.Mode == DynamicBeaconMode.Walk).SlowIntervalSeconds);
    }

    [Fact]
    public async Task MessageServicePersistsHistoryAcrossRestart()
    {
        using var db = TempAppDatabase.Create();
        var portService = new FakePortService();
        var configuration = Substitute.For<IConfigurationService>();
        configuration.Settings.Returns(new AppSettings
        {
            Aprs = new AprsSettings { Callsign = "N0CALL" }
        });

        var portId = Guid.NewGuid();
        var packet = new MessagePacket
        {
            Source = new Callsign("K0PEER", 1),
            Destination = new Callsign("APRS"),
            Addressee = new Callsign("N0CALL"),
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
                   db.Factory))
        {
            // Verify the service loaded the message from database
            var thread = Assert.Single(service.Conversations);
            Assert.Equal(new Callsign("K0PEER", 1), thread.Peer);
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
            db.Factory);
        var threadReloaded = Assert.Single(reloaded.Conversations);
        Assert.Equal(new Callsign("K0PEER", 1), threadReloaded.Peer);
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
        public Task StartAllEnabledPortsAsync() => Task.CompletedTask;
        public Task StopAllPortsAsync() => Task.CompletedTask;
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
            Callsign addressee,
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

        public MessagePacket CreateAckPacket(
            string sourceCallsign,
            Callsign addressee,
            int messageNumber)
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
                Text = $"ack{messageNumber}"
            };
        }

        public MessagePacket CreateRejPacket(
            string sourceCallsign,
            Callsign addressee,
            int messageNumber)
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
                Text = $"rej{messageNumber}"
            };
        }
    }
}
