// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Configuration;
using AetherAprs.Configuration.Settings;
using AetherAprs.Data;
using AetherAprs.Data.Entities;
using AetherAprs.Data.Mappers;
using AetherAprs.Factories.Packets;
using AetherAprs.Models;
using AetherAprs.Models.Aprs;
using AetherAprs.Models.Aprs.Packets;
using AetherAprs.Services.Configuration;
using AetherAprs.Services.Contracts;
using AetherAprs.Services.Messaging;
using AetherAprs.Services.Packets;
using AetherAprs.Services.Ports;
using Geo;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace AetherAprs.Tests.Services.Messaging;

public sealed class BulletinServiceTests
{
    [Fact]
    public async Task SendAsync_TransmitsBulletinOnTxPorts_AndStores()
    {
        var port = CreateTxPort();
        var portService = new FakePortService(port);
        var storage = Substitute.For<IPacketStorageService>();
        var service = CreateBulletinService(portService, storage);

        await service.SendAsync("BLN0SPCL", "Special event today", TestContext.Current.CancellationToken);

        var sent = Assert.Single(portService.SentPackets);
        var packet = Assert.IsType<BulletinPacket>(sent);
        Assert.Equal("BLN0SPCL", packet.Name);
        Assert.Equal("Special event today", packet.Text);

        await storage.Received(1).StorePacketAsync(
            sent,
            port.Id,
            isOutbound: true,
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task SendAsync_NameNotStartingWithBln_Throws()
    {
        var service = CreateBulletinService(new FakePortService(CreateTxPort()), Substitute.For<IPacketStorageService>());

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.SendAsync("SPCL", "text", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SendAsync_NameTooLong_Throws()
    {
        var service = CreateBulletinService(new FakePortService(CreateTxPort()), Substitute.For<IPacketStorageService>());

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.SendAsync("BLNTOOLONG", "text", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SendAsync_TextTooLong_Throws()
    {
        var service = CreateBulletinService(new FakePortService(CreateTxPort()), Substitute.For<IPacketStorageService>());

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.SendAsync("BLN0SPCL", new string('x', 68), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SendAsync_NoTxPorts_Throws()
    {
        var port = new PortConfig
        {
            Id = Guid.NewGuid(),
            Name = "RX-only",
            IsEnabled = true,
            IsTx = false,
            IsRx = true,
            TypeSettings = new AprsIsSettings()
        };
        var service = CreateBulletinService(new FakePortService(port), Substitute.For<IPacketStorageService>());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.SendAsync("BLN0SPCL", "text", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetBulletinsAsync_ReturnsMappedPackets()
    {
        var record = PacketRecordMapper.ToRecord(
            new BulletinPacket
            {
                Source = new Callsign("N0CALL", 1),
                Destination = new Callsign("APRS"),
                Name = "BLN0SPCL",
                Text = "Special event",
                MessageNumber = 5
            },
            null,
            DateTimeOffset.UtcNow,
            isOutbound: false);

        var query = Substitute.For<IPacketQueryService>();
        query.GetBulletinsAsync(Arg.Any<int>(), TestContext.Current.CancellationToken)
            .Returns(new List<PacketRecord> { record });

        var service = CreateBulletinService(
            new FakePortService(CreateTxPort()),
            Substitute.For<IPacketStorageService>(),
            query);

        var bulletins = await service.GetBulletinsAsync(TestContext.Current.CancellationToken);

        var packet = Assert.Single(bulletins);
        Assert.Equal("BLN0SPCL", packet.Name);
        Assert.Equal("Special event", packet.Text);
        Assert.Equal(5, packet.MessageNumber);
    }

    private static PortConfig CreateTxPort()
    {
        return new PortConfig
        {
            Id = Guid.NewGuid(),
            Name = "TX",
            IsEnabled = true,
            IsTx = true,
            IsRx = true,
            TypeSettings = new AprsIsSettings()
        };
    }

    private static BulletinService CreateBulletinService(
        IPortService portService,
        IPacketStorageService storage,
        IPacketQueryService? query = null)
    {
        var configuration = Substitute.For<IConfigurationService>();
        configuration.Settings.Returns(new AppSettings
        {
            Aprs = new AprsSettings
            {
                Callsign = "N0CALL",
                Ssid = (AprsSsid)1
            }
        });

        var resolver = Substitute.For<IAprsPortSettingsResolver>();
        resolver.GetCallsign("N0CALL").Returns("N0CALL-1");

        return new BulletinService(
            portService,
            storage,
            query ?? Substitute.For<IPacketQueryService>(),
            configuration,
            resolver,
            new TestPacketFactory(),
            NullLogger<BulletinService>.Instance);
    }

    private sealed class FakePortService : IPortService
    {
        private readonly List<PortConfig> _ports;

        public FakePortService(params PortConfig[] ports)
        {
            _ports = [.. ports];
        }

        public IReadOnlyList<PortConfig> Ports => _ports;
        public event EventHandler? PortsChanged
        {
            add { }
            remove { }
        }
        public event EventHandler<PortPacketReceivedEventArgs>? PacketReceived;
        public List<AprsPacket> SentPackets { get; } = [];

        public void RaisePacketReceived(PortPacketReceivedEventArgs args) =>
            PacketReceived?.Invoke(this, args);

        public Task SendPacketAsync(Guid id, AprsPacket packet)
        {
            SentPackets.Add(packet);
            return Task.CompletedTask;
        }

        public Task AddPortAsync(PortConfig port) => Task.CompletedTask;
        public Task UpdatePortAsync(PortConfig port) => Task.CompletedTask;
        public Task RemovePortAsync(Guid id) => Task.CompletedTask;
        public Task SetPortEnabledAsync(Guid id, bool enabled) => Task.CompletedTask;
        public Task SetPortShowOnMapAsync(Guid id, bool showOnMap) => Task.CompletedTask;
        public Task StartAllEnabledPortsAsync() => Task.CompletedTask;
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
            return new PositionPacket
            {
                Source = Parse(callsign),
                Destination = new Callsign("APRS"),
                Location = location.Location,
                Symbol = new Symbol(symbolTable, symbolCode)
            };
        }

        public MessagePacket CreateMessagePacket(
            string sourceCallsign,
            AprsAddress addressee,
            string messageText,
            int? messageNumber = null)
        {
            return new MessagePacket
            {
                Source = Parse(sourceCallsign),
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
            return new MessageAckPacket
            {
                Source = Parse(sourceCallsign),
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
            return new MessageRejPacket
            {
                Source = Parse(sourceCallsign),
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
            return new ObjectPacket
            {
                Source = Parse(sourceCallsign),
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
            return new ItemPacket
            {
                Source = Parse(sourceCallsign),
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
            return new BulletinPacket
            {
                Source = Parse(sourceCallsign),
                Destination = new Callsign("APRS"),
                Name = name,
                Text = text,
                MessageNumber = messageNumber
            };
        }

        private static Callsign Parse(string callsign)
        {
            var parts = callsign.Split('-');
            return parts.Length > 1
                ? new Callsign(parts[0], int.Parse(parts[1]))
                : new Callsign(parts[0]);
        }
    }
}
