// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AetherAprs.Configuration;
using AetherAprs.Configuration.Settings;
using AetherAprs.Data;
using AetherAprs.Data.Packets;
using AetherAprs.Factories.Packets;
using AetherAprs.Tests.Helpers;
using AetherAprs.Models;
using AetherAprs.Models.Aprs;
using AetherAprs.Models.Aprs.Packets;
using AetherAprs.Models.Messaging;
using AetherAprs.Services.Configuration;
using AetherAprs.Services.Contracts;
using AetherAprs.Services.Messaging;
using AetherAprs.Services.Packets;
using AetherAprs.Services.Platform;
using AetherAprs.Services.Ports;
using Geo;
using Microsoft.EntityFrameworkCore;
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
        var packetStorageService = Substitute.For<IPacketStorageService>();

        var service = new MessageService(
            portService,
            packetStorageService,
            configuration,
            resolver,
            packetFactory,
            NullLogger<MessageService>.Instance);

        var addressee = AprsAddress.FromCallsign(new Callsign("K0OTH", 7));
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

        // Verify packet was stored to database
        await packetStorageService.Received(1).EnsureOutboundMessageAsync(
            Arg.Any<MessagePacket>(),
            port.Id,
            0,
            Arg.Any<DateTimeOffset?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendAsync_FirstPortStorageFails_OnlyTransmitsAfterNextPortPersists()
    {
        var firstPort = CreateTxPort();
        var secondPort = CreateTxPort();
        var ports = new FakePortService(firstPort, secondPort);
        var storage = Substitute.For<IPacketStorageService>();
        storage.EnsureOutboundMessageAsync(Arg.Any<MessagePacket>(), firstPort.Id, 0,
            Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("simulated write failure")));
        storage.EnsureOutboundMessageAsync(Arg.Any<MessagePacket>(), secondPort.Id, 0,
            Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                Assert.Empty(ports.SentPackets);
                return Task.CompletedTask;
            });
        using var service = CreateMessageService(ports, packetStorageService: storage);

        await service.SendAsync(new AprsAddress("K0OTH"), "Hello", TestContext.Current.CancellationToken);

        Assert.Single(ports.SentPackets);
        await storage.Received(1).EnsureOutboundMessageAsync(Arg.Any<MessagePacket>(), secondPort.Id, 0,
            Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PendingMessage_RestoresAfterProcessRestart_AndRetriesFromDurableOutbox()
    {
        using var database = TempAppDatabase.CreateEmpty();
        using (var setupContext = database.CreateContext())
        {
            setupContext.Database.EnsureCreated();
        }
        var port = CreateTxPort();
        var configuration = CreateConfiguration("N0CALL", defaultSsid: 1);
        var resolver = Substitute.For<IAprsPortSettingsResolver>();
        resolver.GetCallsign("N0CALL").Returns("N0CALL-1");
        await using var writer = new SqliteRuntimeWriter(database.Factory);
        var storage = new PacketStorageService(
            writer,
            configuration,
            NullLogger<PacketStorageService>.Instance);

        var failedPortService = new FakePortService(port) { FailSends = true };
        var firstProcess = new MessageService(
            failedPortService,
            storage,
            configuration,
            resolver,
            new TestPacketFactory(),
            NullLogger<MessageService>.Instance,
            new PacketQueryService(new SqliteRuntimeReader(database.Factory), NullLogger<PacketQueryService>.Instance));

        await firstProcess.SendAsync(
            AprsAddress.FromCallsign(new Callsign("K0OTH")),
            "Survive restart",
            TestContext.Current.CancellationToken);
        firstProcess.Dispose();

        using (var context = database.CreateContext())
        {
            var queued = Assert.Single(context.Packets);
            Assert.True(queued.IsOutbound);
            Assert.Equal(MessageDeliveryStatus.Pending, queued.Message!.DeliveryStatus);
            Assert.NotNull(queued.Message.NextRetryTime);
        }

        var recoveredPortService = new FakePortService(port);
        var recoveredProcess = new MessageService(
            recoveredPortService,
            storage,
            configuration,
            resolver,
            new TestPacketFactory(),
            NullLogger<MessageService>.Instance,
            new PacketQueryService(new SqliteRuntimeReader(database.Factory), NullLogger<PacketQueryService>.Instance));
        await recoveredProcess.InitializeAsync(TestContext.Current.CancellationToken);

        var recoveredMessage = Assert.Single(Assert.Single(recoveredProcess.Conversations).Messages);
        recoveredMessage.NextRetryTime = DateTimeOffset.UtcNow.AddSeconds(-1);
        recoveredProcess.ProcessRetries(null);
        await recoveredPortService.PacketSent.Task.WaitAsync(TestTimeouts.TestTimeout, TestContext.Current.CancellationToken);

        Assert.Single(recoveredPortService.SentPackets);
        recoveredProcess.Dispose();
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
                Addressee = AprsAddress.FromCallsign(new Callsign("N0CALL")),
                Text = "Ping"
            }
        });

        var thread = Assert.Single(service.Conversations);
        Assert.Equal(AprsAddress.FromCallsign(new Callsign("K0PEER", 1)), thread.Peer);
        var stored = Assert.Single(thread.Messages);
        Assert.False(stored.IsOutbound);
        Assert.Equal("Ping", stored.Text);
    }

    [Fact]
    public async Task PacketReceived_NotViewingConversation_PostsNotification()
    {
        var portService = new FakePortService();
        var notificationService = Substitute.For<INotificationService>();
        var service = new MessageService(
            portService,
            Substitute.For<IPacketStorageService>(),
            CreateConfiguration("N0CALL"),
            Substitute.For<IAprsPortSettingsResolver>(),
            new TestPacketFactory(),
            NullLogger<MessageService>.Instance,
            notificationService: notificationService);

        var peer = AprsAddress.FromCallsign(new Callsign("K0PEER", 1));
        portService.RaisePacketReceived(new PortPacketReceivedEventArgs
        {
            PortId = Guid.NewGuid(),
            Packet = new MessagePacket
            {
                Source = new Callsign("K0PEER", 1),
                Destination = new Callsign("APRS"),
                Addressee = AprsAddress.FromCallsign(new Callsign("N0CALL")),
                Text = "Ping"
            }
        });

        // Notification is fire-and-forget; give it a moment to complete.
        await Task.Delay(50, TestContext.Current.CancellationToken);

        await notificationService.Received(1).ShowMessageNotificationAsync(
            "K0PEER-1",
            "Ping",
            "K0PEER-1",
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PacketReceived_ViewingConversation_SuppressesNotification()
    {
        var portService = new FakePortService();
        var notificationService = Substitute.For<INotificationService>();
        var service = new MessageService(
            portService,
            Substitute.For<IPacketStorageService>(),
            CreateConfiguration("N0CALL"),
            Substitute.For<IAprsPortSettingsResolver>(),
            new TestPacketFactory(),
            NullLogger<MessageService>.Instance,
            notificationService: notificationService);

        var peer = AprsAddress.FromCallsign(new Callsign("K0PEER", 1));
        service.SetActiveConversation(peer);

        portService.RaisePacketReceived(new PortPacketReceivedEventArgs
        {
            PortId = Guid.NewGuid(),
            Packet = new MessagePacket
            {
                Source = new Callsign("K0PEER", 1),
                Destination = new Callsign("APRS"),
                Addressee = AprsAddress.FromCallsign(new Callsign("N0CALL")),
                Text = "Ping"
            }
        });

        await Task.Delay(50, TestContext.Current.CancellationToken);

        await notificationService.DidNotReceive().ShowMessageNotificationAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ClearActiveConversation_MatchingPeer_ClearsState()
    {
        var portService = new FakePortService();
        var notificationService = Substitute.For<INotificationService>();
        var service = new MessageService(
            portService,
            Substitute.For<IPacketStorageService>(),
            CreateConfiguration("N0CALL"),
            Substitute.For<IAprsPortSettingsResolver>(),
            new TestPacketFactory(),
            NullLogger<MessageService>.Instance,
            notificationService: notificationService);

        var peer = AprsAddress.FromCallsign(new Callsign("K0PEER", 1));
        service.SetActiveConversation(peer);
        service.ClearActiveConversation(peer);

        portService.RaisePacketReceived(new PortPacketReceivedEventArgs
        {
            PortId = Guid.NewGuid(),
            Packet = new MessagePacket
            {
                Source = new Callsign("K0PEER", 1),
                Destination = new Callsign("APRS"),
                Addressee = AprsAddress.FromCallsign(new Callsign("N0CALL")),
                Text = "Ping"
            }
        });

        // After clearing, the incoming message should post a notification again.
        notificationService.Received(1).ShowMessageNotificationAsync(
            "K0PEER-1",
            "Ping",
            "K0PEER-1",
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ClearActiveConversation_DifferentPeer_DoesNotClear()
    {
        var portService = new FakePortService();
        var notificationService = Substitute.For<INotificationService>();
        var service = new MessageService(
            portService,
            Substitute.For<IPacketStorageService>(),
            CreateConfiguration("N0CALL"),
            Substitute.For<IAprsPortSettingsResolver>(),
            new TestPacketFactory(),
            NullLogger<MessageService>.Instance,
            notificationService: notificationService);

        var viewedCallsign = new Callsign("K0PEER", 1);
        var viewed = AprsAddress.FromCallsign(viewedCallsign);
        var otherCallsign = new Callsign("K0OTH", 2);
        var other = AprsAddress.FromCallsign(otherCallsign);
        service.SetActiveConversation(viewed);

        // Clearing a different peer (e.g. retiring a stale overlay) must NOT
        // clear the conversation actually being viewed.
        service.ClearActiveConversation(other);

        portService.RaisePacketReceived(new PortPacketReceivedEventArgs
        {
            PortId = Guid.NewGuid(),
            Packet = new MessagePacket
            {
                Source = viewedCallsign,
                Destination = new Callsign("APRS"),
                Addressee = AprsAddress.FromCallsign(new Callsign("N0CALL")),
                Text = "Ping"
            }
        });

        notificationService.DidNotReceive().ShowMessageNotificationAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
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
            () => service.SendAsync(AprsAddress.FromCallsign(new Callsign("K0OTH")), "Hello", TestContext.Current.CancellationToken));
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
            () => service.SendAsync(AprsAddress.FromCallsign(new Callsign("K0OTH")), "Hello", TestContext.Current.CancellationToken));
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
            () => service.SendAsync(AprsAddress.FromCallsign(new Callsign("K0OTH")), longText, TestContext.Current.CancellationToken));
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
                Addressee = AprsAddress.FromCallsign(new Callsign("OTHER")),
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

        var addressee = AprsAddress.FromCallsign(new Callsign("K0OTH", 7));

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
                        Addressee = AprsAddress.FromCallsign(new Callsign("N0CALL")),
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

        var callsign = AprsAddress.FromCallsign(new Callsign("K0OTH", 7));
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

    [Fact]
    public async Task MessageAck_FromPeer_MarksMessageAcknowledged()
    {
        var port = CreateTxPort();
        var portService = new FakePortService(port);
        var service = CreateMessageService(portService);

        var peerCallsign = new Callsign("K0OTH", 7);
        var peer = AprsAddress.FromCallsign(peerCallsign);
        await service.SendAsync(peer, "Hello", TestContext.Current.CancellationToken);

        var thread = Assert.Single(service.Conversations);
        var stored = Assert.Single(thread.Messages);
        Assert.NotNull(stored.MessageNumber);

        portService.RaisePacketReceived(new PortPacketReceivedEventArgs
        {
            PortId = port.Id,
            Packet = new MessageAckPacket
            {
                Source = peerCallsign,
                Destination = new Callsign("APRS"),
                Addressee = AprsAddress.FromCallsign(new Callsign("N0CALL", 1)),
                MessageNumber = stored.MessageNumber.Value
            }
        });

        Assert.Equal(MessageDeliveryStatus.Acknowledged, stored.DeliveryStatus);
    }

    [Fact]
    public async Task MessageAck_FromWrongSource_LeavesMessagePending()
    {
        var port = CreateTxPort();
        var portService = new FakePortService(port);
        var service = CreateMessageService(portService);

        var peer = AprsAddress.FromCallsign(new Callsign("K0OTH", 7));
        await service.SendAsync(peer, "Hello", TestContext.Current.CancellationToken);

        var thread = Assert.Single(service.Conversations);
        var stored = Assert.Single(thread.Messages);
        Assert.NotNull(stored.MessageNumber);

        portService.RaisePacketReceived(new PortPacketReceivedEventArgs
        {
            PortId = port.Id,
            Packet = new MessageAckPacket
            {
                Source = new Callsign("K0IMP", 1),
                Destination = new Callsign("APRS"),
                Addressee = AprsAddress.FromCallsign(new Callsign("N0CALL", 1)),
                MessageNumber = stored.MessageNumber.Value
            }
        });

        Assert.Equal(MessageDeliveryStatus.Pending, stored.DeliveryStatus);
    }

    [Fact]
    public async Task MessageAck_UnknownMessageNumber_Ignored()
    {
        var port = CreateTxPort();
        var portService = new FakePortService(port);
        var service = CreateMessageService(portService);

        var peer = AprsAddress.FromCallsign(new Callsign("K0OTH", 7));
        await service.SendAsync(peer, "Hello", TestContext.Current.CancellationToken);

        portService.RaisePacketReceived(new PortPacketReceivedEventArgs
        {
            PortId = port.Id,
            Packet = new MessageAckPacket
            {
                Source = new Callsign("K0PEER", 1),
                Destination = new Callsign("APRS"),
                Addressee = AprsAddress.FromCallsign(new Callsign("N0CALL", 1)),
                MessageNumber = 999
            }
        });

        var thread = Assert.Single(service.Conversations);
        var stored = Assert.Single(thread.Messages);
        Assert.Equal(MessageDeliveryStatus.Pending, stored.DeliveryStatus);
    }

    [Fact]
    public async Task MessageRej_FromPeer_MarksMessageRejected()
    {
        var port = CreateTxPort();
        var portService = new FakePortService(port);
        var service = CreateMessageService(portService);

        var peerCallsign = new Callsign("K0OTH", 7);
        var peer = AprsAddress.FromCallsign(peerCallsign);
        await service.SendAsync(peer, "Hello", TestContext.Current.CancellationToken);

        var thread = Assert.Single(service.Conversations);
        var stored = Assert.Single(thread.Messages);
        Assert.NotNull(stored.MessageNumber);

        portService.RaisePacketReceived(new PortPacketReceivedEventArgs
        {
            PortId = port.Id,
            Packet = new MessageRejPacket
            {
                Source = peerCallsign,
                Destination = new Callsign("APRS"),
                Addressee = AprsAddress.FromCallsign(new Callsign("N0CALL", 1)),
                MessageNumber = stored.MessageNumber.Value
            }
        });

        Assert.Equal(MessageDeliveryStatus.Rejected, stored.DeliveryStatus);
    }

    [Fact]
    public async Task InboundMessage_WithNumberAndAutoAckEnabled_SendsAckOnReceivingPort()
    {
        var port = CreateTxPort();
        var portService = new FakePortService(port);
        var configuration = Substitute.For<IConfigurationService>();
        configuration.Settings.Returns(new AppSettings
        {
            Aprs = new AprsSettings
            {
                Callsign = "N0CALL",
                AutoAcknowledgeMessages = true
            }
        });
        var resolver = Substitute.For<IAprsPortSettingsResolver>();
        resolver.GetCallsign("N0CALL").Returns("N0CALL-1");
        var service = new MessageService(
            portService,
            Substitute.For<IPacketStorageService>(),
            configuration,
            resolver,
            new TestPacketFactory(),
            NullLogger<MessageService>.Instance);

        var peerCallsign = new Callsign("K0OTH", 7);
        var peer = AprsAddress.FromCallsign(peerCallsign);
        portService.RaisePacketReceived(new PortPacketReceivedEventArgs
        {
            PortId = port.Id,
            Packet = new MessagePacket
            {
                Source = peerCallsign,
                Destination = new Callsign("N0CALL", 1),
                Addressee = AprsAddress.FromCallsign(new Callsign("N0CALL")),
                Text = "Ping",
                MessageNumber = 5
            }
        });

        var ack = Assert.IsType<MessageAckPacket>(
            await portService.PacketSent.Task.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken));
        Assert.Equal(peer, ack.Addressee);
        Assert.Equal(5, ack.MessageNumber);
        Assert.Equal(new Callsign("N0CALL", 1), ack.Source);
    }

    [Fact]
    public async Task ProcessRetries_DueMessage_ResendsWithIncrementedRetryCount()
    {
        var port = CreateTxPort();
        var portService = new FakePortService(port);
        var service = CreateMessageService(portService);

        var peer = AprsAddress.FromCallsign(new Callsign("K0OTH", 7));
        await service.SendAsync(peer, "Hello", TestContext.Current.CancellationToken);

        var thread = Assert.Single(service.Conversations);
        var stored = Assert.Single(thread.Messages);
        Assert.Equal(0, stored.RetryCount);

        // Make the message due for retry, then run the retry pass directly
        // (the real timer ticks every 5 s — too slow for tests).
        stored.NextRetryTime = DateTimeOffset.UtcNow.AddSeconds(-1);
        service.ProcessRetries(null);

        var resent = Assert.IsType<MessagePacket>(
            await portService.PacketSent.Task.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken));
        Assert.Equal(stored.MessageNumber, resent.MessageNumber);
        Assert.Equal(1, stored.RetryCount);
        Assert.NotNull(stored.NextRetryTime);
        Assert.True(stored.NextRetryTime.Value > DateTimeOffset.UtcNow.AddSeconds(-5));
    }

    [Fact]
    public async Task ProcessRetries_RetriesExhausted_MarksMessageTimeout()
    {
        var port = CreateTxPort();
        var portService = new FakePortService(port);
        var service = CreateMessageService(portService);

        var peer = AprsAddress.FromCallsign(new Callsign("K0OTH", 7));
        await service.SendAsync(peer, "Hello", TestContext.Current.CancellationToken);

        var thread = Assert.Single(service.Conversations);
        var stored = Assert.Single(thread.Messages);
        stored.RetryCount = 5; // MessageMaxRetries default is 5
        stored.NextRetryTime = DateTimeOffset.UtcNow.AddSeconds(-1);

        service.ProcessRetries(null);

        Assert.Equal(MessageDeliveryStatus.Timeout, stored.DeliveryStatus);

        // A subsequent pass must not resend the timed-out message.
        await Task.Delay(200, TestContext.Current.CancellationToken);
        Assert.Single(portService.SentPackets);
    }

    [Fact]
    public async Task RetryingMessage_StoresOneOutboundRecordPerLogicalMessage()
    {
        var port = CreateTxPort();
        var portService = new FakePortService(port);
        using var db = TempAppDatabase.Create();
        var storageConfiguration = Substitute.For<IConfigurationService>();
        storageConfiguration.Settings.Returns(new AppSettings
        {
            Aprs = new AprsSettings { Callsign = "N0CALL", Ssid = (AprsSsid)1 }
        });
        await using var writer = new SqliteRuntimeWriter(db.Factory);
        var storage = new PacketStorageService(
            writer,
            storageConfiguration,
            NullLogger<PacketStorageService>.Instance);
        var service = CreateMessageService(portService, db.Factory, storage);

        var peer = AprsAddress.FromCallsign(new Callsign("K0OTH", 7));
        await service.SendAsync(peer, "Hello", TestContext.Current.CancellationToken);

        var thread = Assert.Single(service.Conversations);
        var stored = Assert.Single(thread.Messages);

        // MessageMaxRetries defaults to 5; one extra pass marks the message timed
        // out, which is the terminal state that cleanup is allowed to delete.
        const int retryPasses = 6;
        for (var attempt = 1; attempt <= retryPasses; attempt++)
        {
            stored.NextRetryTime = DateTimeOffset.UtcNow.AddSeconds(-1);
            service.ProcessRetries(null);
            if (attempt < retryPasses)
            {
                await WaitUntilAsync(
                    () => portService.SentPackets.Count >= attempt + 1,
                    $"retry send {attempt}");
            }
        }

        Assert.Equal(MessageDeliveryStatus.Timeout, stored.DeliveryStatus);
        Assert.Equal(5, stored.RetryCount);

        Assert.Equal(retryPasses, portService.SentPackets.Count);

        // Status persistence runs on the thread pool, so the durable row can still
        // read Pending right after the in-memory status flips to Timeout.
        await WaitUntilAsync(async () =>
        {
            await using var context = db.CreateContext();
            var row = await context.Packets
                .AsNoTracking()
                .Include(p => p.Message)
                .FirstOrDefaultAsync(
                    p => p.IsOutbound && p.Message != null && p.Message.Number == stored.MessageNumber,
                    TestContext.Current.CancellationToken);
            return row?.Message?.DeliveryStatus == MessageDeliveryStatus.Timeout;
        }, "the outbound row to reach its terminal status");

        var outboundRows = await CountOutboundMessageRowsAsync(db, stored.MessageNumber, peer);
        Assert.Equal(1, outboundRows);

        await using var verification = db.CreateContext();
        var record = await verification.Packets
            .Include(p => p.Message)
            .SingleAsync(
                p => p.IsOutbound && p.Message != null && p.Message.Number == stored.MessageNumber,
                TestContext.Current.CancellationToken);
        Assert.Equal(MessageDeliveryStatus.Timeout, record.Message!.DeliveryStatus);
        Assert.Equal(5, record.Message.RetryCount);
    }

    private static async Task<int> CountOutboundMessageRowsAsync(
        TempAppDatabase db,
        int? messageNumber,
        AprsAddress peer)
    {
        await using var context = db.CreateContext();
        return await context.Packets
            .Where(p => p.IsOutbound && p.Message != null && p.Message.Number == messageNumber)
            .CountAsync(TestContext.Current.CancellationToken);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, string description)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
            {
                Assert.Fail($"Timed out waiting for {description}.");
            }

            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition, string description)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
        while (!await condition())
        {
            if (DateTime.UtcNow >= deadline)
            {
                Assert.Fail($"Timed out waiting for {description}.");
            }

            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
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

    private static MessageService CreateMessageService(
        FakePortService portService,
        IDbContextFactory<AppDbContext>? dbContextFactory = null,
        IPacketStorageService? packetStorageService = null)
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

        return new MessageService(
            portService,
            packetStorageService ?? Substitute.For<IPacketStorageService>(),
            configuration,
            resolver,
            new TestPacketFactory(),
            NullLogger<MessageService>.Instance,
            dbContextFactory is null
                ? null
                : new PacketQueryService(new SqliteRuntimeReader(dbContextFactory), NullLogger<PacketQueryService>.Instance));
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

        public bool FailSends { get; set; }

        /// <summary>Signalled whenever a packet is sent — lets tests await deterministic sends.</summary>
        public TaskCompletionSource<AprsPacket> PacketSent { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void RaisePacketReceived(PortPacketReceivedEventArgs args) =>
            PacketReceived?.Invoke(this, args);

        public Task SendPacketAsync(Guid id, AprsPacket packet)
        {
            if (FailSends)
            {
                throw new InvalidOperationException("simulated transport loss");
            }

            SentPackets.Add(packet);
            PacketSent.TrySetResult(packet);
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
