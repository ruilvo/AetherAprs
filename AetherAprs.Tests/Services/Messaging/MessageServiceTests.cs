// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AetherAprs.Configuration;
using AetherAprs.Configuration.Settings;
using AetherAprs.Factories.Packets;
using AetherAprs.Models;
using AetherAprs.Models.Aprs;
using AetherAprs.Models.Aprs.Packets;
using AetherAprs.Models.Messaging;
using AetherAprs.Services.Configuration;
using AetherAprs.Services.Contracts;
using AetherAprs.Services.Messaging;
using AetherAprs.Services.Packets;
using AetherAprs.Services.Ports;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace AetherAprs.Tests.Services.Messaging;

public sealed class MessageServiceTests
{
    [Fact]
    public async Task SendAsync_SendsMessagePacketOnEnabledTxPorts_AndStoresOutbound()
    {
        var port = new PortConfig
        {
            Id = Guid.NewGuid(),
            Name = "TX",
            IsEnabled = true,
            IsTx = true,
            IsRx = true,
            TypeSettings = new AprsIsSettings()
        };
        var portService = new FakePortService(port);
        var configuration = CreateConfiguration("N0CALL", defaultSsid: 1);
        var resolver = Substitute.For<IAprsPortSettingsResolver>();
        resolver.GetCallsign("N0CALL").Returns("N0CALL-1");
        var packetFactory = new TestPacketFactory();

        var service = new MessageService(
            portService,
            Substitute.For<IPacketStorageService>(),
            configuration,
            resolver,
            packetFactory,
            NullLogger<MessageService>.Instance);

        var addressee = new Callsign("K0OTH", 7);
        await service.SendAsync(addressee, "Hello", TestContext.Current.CancellationToken);

        var message = Assert.IsType<MessagePacket>(Assert.Single(portService.SentPackets));
        Assert.Equal(new Callsign("N0CALL", 1), message.Source);
        Assert.Equal(addressee, message.Addressee);
        Assert.Equal("Hello", message.Text);
        Assert.NotNull(message.MessageNumber);

        var thread = Assert.Single(service.Conversations);
        Assert.Equal(addressee, thread.Peer);
        var stored = Assert.Single(thread.Messages);
        Assert.True(stored.IsOutbound);
        Assert.Equal("Hello", stored.Text);
    }

    [Fact]
    public void PacketReceived_StoresInboundMessageAddressedToUs()
    {
        var portService = new FakePortService();
        var configuration = CreateConfiguration("N0CALL");
        var packetFactory = new TestPacketFactory();
        var service = new MessageService(
            portService,
            Substitute.For<IPacketStorageService>(),
            configuration,
            Substitute.For<IAprsPortSettingsResolver>(),
            packetFactory,
            NullLogger<MessageService>.Instance);

        portService.RaisePacketReceived(new PortPacketReceivedEventArgs
        {
            PortId = Guid.NewGuid(),
            Packet = new MessagePacket
            {
                Source = new Callsign("K0PEER", 1),
                Destination = new Callsign("APRS"),
                Addressee = new Callsign("N0CALL"),
                Text = "Ping"
            }
        });

        var thread = Assert.Single(service.Conversations);
        Assert.Equal(new Callsign("K0PEER", 1), thread.Peer);
        var stored = Assert.Single(thread.Messages);
        Assert.False(stored.IsOutbound);
        Assert.Equal("Ping", stored.Text);
    }



    [Fact]
    public async Task SendAsync_EnabledPortWithIsTxFalse_ThrowsNoTxPorts()
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
        var packetFactory = new TestPacketFactory();
        var service = new MessageService(
            new FakePortService(port),
            Substitute.For<IPacketStorageService>(),
            CreateConfiguration("N0CALL"),
            Substitute.For<IAprsPortSettingsResolver>(),
            packetFactory,
            NullLogger<MessageService>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.SendAsync(new Callsign("K0OTH"), "Hello", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SendAsync_ThrowsWhenNoTxPorts()
    {
        var portService = new FakePortService();
        var packetFactory = new TestPacketFactory();
        var service = new MessageService(
            portService,
            Substitute.For<IPacketStorageService>(),
            CreateConfiguration("N0CALL"),
            Substitute.For<IAprsPortSettingsResolver>(),
            packetFactory,
            NullLogger<MessageService>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.SendAsync(new Callsign("K0OTH"), "Hello", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SendAsync_ThrowsWhenTextTooLong()
    {
        var port = new PortConfig
        {
            Id = Guid.NewGuid(),
            Name = "TX",
            IsEnabled = true,
            IsTx = true,
            TypeSettings = new AprsIsSettings()
        };
        var packetFactory = new TestPacketFactory();
        var service = new MessageService(
            new FakePortService(port),
            Substitute.For<IPacketStorageService>(),
            CreateConfiguration("N0CALL"),
            Substitute.For<IAprsPortSettingsResolver>(),
            packetFactory,
            NullLogger<MessageService>.Instance);

        var longText = new string('A', 68);
        await Assert.ThrowsAsync<ArgumentException>(
            () => service.SendAsync(new Callsign("K0OTH"), longText, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void PacketReceived_IgnoresMessagesNotAddressedToUs()
    {
        var portService = new FakePortService();
        var packetFactory = new TestPacketFactory();
        var service = new MessageService(
            portService,
            Substitute.For<IPacketStorageService>(),
            CreateConfiguration("N0CALL"),
            Substitute.For<IAprsPortSettingsResolver>(),
            packetFactory,
            NullLogger<MessageService>.Instance);

        portService.RaisePacketReceived(new PortPacketReceivedEventArgs
        {
            PortId = Guid.NewGuid(),
            Packet = new MessagePacket
            {
                Source = new Callsign("K0PEER"),
                Destination = new Callsign("APRS"),
                Addressee = new Callsign("OTHER"),
                Text = "Nope"
            }
        });

        Assert.Empty(service.Conversations);
    }

    [Fact]
    public async Task SendAsync_ConcurrentCalls_AssignsUniqueMessageNumbers()
    {
        // Arrange
        var port = new PortConfig
        {
            Id = Guid.NewGuid(),
            Name = "TX",
            IsEnabled = true,
            IsTx = true,
            IsRx = true,
            TypeSettings = new AprsIsSettings()
        };
        var portService = new FakePortService(port);
        var resolver = Substitute.For<IAprsPortSettingsResolver>();
        resolver.GetCallsign("N0CALL").Returns("N0CALL-1");
        var packetFactory = new TestPacketFactory();

        var service = new MessageService(
            portService,
            Substitute.For<IPacketStorageService>(),
            CreateConfiguration("N0CALL", defaultSsid: 1),
            resolver,
            packetFactory,
            NullLogger<MessageService>.Instance);

        var addressee = new Callsign("K0OTH", 7);

        // Act - send 10 messages concurrently
        var tasks = Enumerable.Range(1, 10)
            .Select(i => service.SendAsync(addressee, $"Message {i}", TestContext.Current.CancellationToken))
            .ToArray();

        await Task.WhenAll(tasks);

        // Assert - all message numbers should be unique
        var messageNumbers = portService.SentPackets
            .OfType<MessagePacket>()
            .Select(p => p.MessageNumber)
            .ToList();

        Assert.Equal(10, messageNumbers.Count);
        Assert.Equal(10, messageNumbers.Distinct().Count()); // All unique
    }

    [Fact]
    public async Task PacketReceived_ConcurrentPackets_CreatesConversationsThreadSafely()
    {
        // Arrange
        var portService = new FakePortService();
        var packetFactory = new TestPacketFactory();
        var service = new MessageService(
            portService,
            Substitute.For<IPacketStorageService>(),
            CreateConfiguration("N0CALL"),
            Substitute.For<IAprsPortSettingsResolver>(),
            packetFactory,
            NullLogger<MessageService>.Instance);

        // Act - receive 100 packets concurrently from different callsigns
        var tasks = Enumerable.Range(1, 100)
            .Select(i => Task.Run(() =>
            {
                // Generate callsigns: K1A to K100 format, ensuring 2-6 chars
                var callsign = i < 10 ? $"K{i}A" : i < 100 ? $"K{i}" : "K100";
                portService.RaisePacketReceived(new PortPacketReceivedEventArgs
                {
                    PortId = Guid.NewGuid(),
                    Packet = new MessagePacket
                    {
                        Source = new Callsign(callsign, (i % 15) + 1), // Valid SSID range 1-15
                        Destination = new Callsign("APRS"),
                        Addressee = new Callsign("N0CALL"),
                        Text = $"Message {i}"
                    }
                });
            }, TestContext.Current.CancellationToken))
            .ToArray();

        await Task.WhenAll(tasks);

        // Assert - should have 100 conversations, one per unique callsign
        Assert.Equal(100, service.Conversations.Count);
        
        // Verify each conversation has exactly one message
        foreach (var conversation in service.Conversations)
        {
            Assert.Single(conversation.Messages);
        }
    }

    [Fact]
    public async Task GetOrCreateConversation_ConcurrentAccess_ReturnsSameInstanceForSameCallsign()
    {
        // Arrange
        var portService = new FakePortService();
        var packetFactory = new TestPacketFactory();
        var service = new MessageService(
            portService,
            Substitute.For<IPacketStorageService>(),
            CreateConfiguration("N0CALL"),
            Substitute.For<IAprsPortSettingsResolver>(),
            packetFactory,
            NullLogger<MessageService>.Instance);

        var callsign = new Callsign("K0OTH", 7);
        var conversations = new ConcurrentBag<ConversationThread>();

        // Act - call GetOrCreateConversation 100 times concurrently
        var tasks = Enumerable.Range(1, 100)
            .Select(_ => Task.Run(() =>
            {
                var conversation = service.GetOrCreateConversation(callsign);
                conversations.Add(conversation);
            }, TestContext.Current.CancellationToken))
            .ToArray();

        await Task.WhenAll(tasks);

        // Assert - should have only 1 conversation in service
        Assert.Single(service.Conversations);
        
        // All returned instances should be the same reference
        var distinctConversations = conversations.Distinct().ToList();
        Assert.Single(distinctConversations);
    }

    private static IConfigurationService CreateConfiguration(string callsign, int defaultSsid = 0)
    {
        var configuration = Substitute.For<IConfigurationService>();
        configuration.Settings.Returns(new AppSettings
        {
            Aprs = new AprsSettings
            {
                Callsign = callsign,
                Ssid = (AprsSsid)defaultSsid
            }
        });
        return configuration;
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
