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
using AetherAprs.ViewModels;
using Geo;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;
using AetherAprs.Data.Entities;

namespace AetherAprs.Tests.Services.Messaging;

public sealed class MessageServiceTests
{
    // Defaults from AprsSettings: five retries and a 30 s initial backoff that
    // doubles per attempt. The expiry tick lands one initial interval after the
    // summed schedule.
    private const int RetryTimeoutSeconds = 30;
    private const int MaxRetries = 5;
    private const int ExpirySeconds = RetryTimeoutSeconds * (1 << MaxRetries);

    [Fact]
    public async Task SendAsync_SendsMessagePacketOnEnabledTxPorts_AndStoresOutbound()
    {
        PortConfig port = new()
        {
            Id = Guid.NewGuid(),
            Name = "TX",
            IsEnabled = true,
            IsTx = true,
            IsRx = true,
            TypeSettings = new AprsIsSettings()
        };
        FakePortService portService = new(port);
        IConfigurationService configuration = CreateConfiguration("N0CALL", defaultSsid: 1);
        IAprsPortSettingsResolver resolver = Substitute.For<IAprsPortSettingsResolver>();
        resolver.GetCallsign("N0CALL").Returns("N0CALL-1");
        TestPacketFactory packetFactory = new();
        IPacketStorageService packetStorageService = Substitute.For<IPacketStorageService>();

        MessageService service = new(
            portService,
            packetStorageService,
            configuration,
            resolver,
            packetFactory,
            NullLogger<MessageService>.Instance,
            SynchronousUiDispatcher.Instance);

        AprsAddress addressee = AprsAddress.FromCallsign(new Callsign("K0OTH", 7));
        await service.SendAsync(addressee, "Hello", TestContext.Current.CancellationToken);

        MessagePacket message = Assert.IsType<MessagePacket>(Assert.Single(portService.SentPackets));
        Assert.Equal(new Callsign("N0CALL", 1), message.Source);
        Assert.Equal(addressee, message.Addressee);
        Assert.Equal("Hello", message.Text);
        Assert.NotNull(message.MessageNumber);

        ConversationThread thread = Assert.Single(service.Conversations);
        Assert.Equal(addressee, thread.Peer);
        StoredMessage stored = Assert.Single(thread.Messages);
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
        PortConfig firstPort = CreateTxPort();
        PortConfig secondPort = CreateTxPort();
        FakePortService ports = new(firstPort, secondPort);
        IPacketStorageService storage = Substitute.For<IPacketStorageService>();
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
        using MessageService service = CreateMessageService(ports, packetStorageService: storage);

        await service.SendAsync(new AprsAddress("K0OTH"), "Hello", TestContext.Current.CancellationToken);

        Assert.Single(ports.SentPackets);
        await storage.Received(1).EnsureOutboundMessageAsync(Arg.Any<MessagePacket>(), secondPort.Id, 0,
            Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PendingMessage_RestoresAfterProcessRestart_AndRetriesFromDurableOutbox()
    {
        using TempAppDatabase database = TempAppDatabase.CreateEmpty();
        using (AppDbContext setupContext = database.CreateContext())
        {
            setupContext.Database.EnsureCreated();
        }
        PortConfig port = CreateTxPort();
        IConfigurationService configuration = CreateConfiguration("N0CALL", defaultSsid: 1);
        IAprsPortSettingsResolver resolver = Substitute.For<IAprsPortSettingsResolver>();
        resolver.GetCallsign("N0CALL").Returns("N0CALL-1");
        await using SqliteRuntimeWriter writer = new(database.Factory);
        PacketStorageService storage = new(
            writer,
            configuration,
            NullLogger<PacketStorageService>.Instance);

        FakePortService failedPortService = new(port) { FailSends = true };
        MessageService firstProcess = new(
            failedPortService,
            storage,
            configuration,
            resolver,
            new TestPacketFactory(),
            NullLogger<MessageService>.Instance,
            SynchronousUiDispatcher.Instance,
            new PacketQueryService(new SqliteRuntimeReader(database.Factory), NullLogger<PacketQueryService>.Instance));

        await firstProcess.SendAsync(
            AprsAddress.FromCallsign(new Callsign("K0OTH")),
            "Survive restart",
            TestContext.Current.CancellationToken);
        firstProcess.Dispose();

        using (AppDbContext context = database.CreateContext())
        {
            PacketRecord queued = Assert.Single(context.Packets);
            Assert.True(queued.IsOutbound);
            Assert.Equal(MessageDeliveryStatus.Pending, queued.Message!.DeliveryStatus);
            Assert.NotNull(queued.Message.NextRetryTime);
        }

        FakePortService recoveredPortService = new(port);
        MessageService recoveredProcess = new(
            recoveredPortService,
            storage,
            configuration,
            resolver,
            new TestPacketFactory(),
            NullLogger<MessageService>.Instance,
            SynchronousUiDispatcher.Instance,
            new PacketQueryService(new SqliteRuntimeReader(database.Factory), NullLogger<PacketQueryService>.Instance));
        await recoveredProcess.InitializeAsync(TestContext.Current.CancellationToken);

        StoredMessage recoveredMessage = Assert.Single(Assert.Single(recoveredProcess.Conversations).Messages);
        recoveredMessage.NextRetryTime = DateTimeOffset.UtcNow.AddSeconds(-1);
        recoveredProcess.ProcessRetries(null);
        await recoveredPortService.PacketSent.Task.WaitAsync(TestTimeouts.TestTimeout, TestContext.Current.CancellationToken);

        Assert.Single(recoveredPortService.SentPackets);
        recoveredProcess.Dispose();
    }

    [Fact]
    public void PacketReceived_StoresInboundMessageAddressedToUs()
    {
        FakePortService portService = new();
        IConfigurationService configuration = CreateConfiguration("N0CALL");
        TestPacketFactory packetFactory = new();
        MessageService service = new(
            portService,
            Substitute.For<IPacketStorageService>(),
            configuration,
            Substitute.For<IAprsPortSettingsResolver>(),
            packetFactory,
            NullLogger<MessageService>.Instance,
            SynchronousUiDispatcher.Instance);

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

        ConversationThread thread = Assert.Single(service.Conversations);
        Assert.Equal(AprsAddress.FromCallsign(new Callsign("K0PEER", 1)), thread.Peer);
        StoredMessage stored = Assert.Single(thread.Messages);
        Assert.False(stored.IsOutbound);
        Assert.Equal("Ping", stored.Text);
    }

    [Fact]
    public void PacketReceived_ReportsTheMessageWithoutItsText()
    {
        FakePortService portService = new();
        RecordingLogger<MessageService> logger = new();
        _ = CreateMessageService(portService, logger: logger);
        portService.RaisePacketReceived(new PortPacketReceivedEventArgs
        {
            PortId = Guid.NewGuid(),
            Packet = new MessagePacket
            {
                Source = new Callsign("K0PEER", 1),
                Destination = new Callsign("APRS"),
                Addressee = AprsAddress.FromCallsign(new Callsign("N0CALL")),
                Text = "PRIVATE-BODY-4321"
            }
        });

        Assert.Contains(logger.Entries, entry =>
            entry.Message.Contains("K0PEER-1", StringComparison.Ordinal));
        Assert.DoesNotContain(logger.Entries, entry =>
            entry.Message.Contains("PRIVATE-BODY-4321", StringComparison.Ordinal));
        Assert.DoesNotContain(logger.Entries, entry =>
            entry.Exception?.ToString().Contains("PRIVATE-BODY-4321", StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task PacketReceived_NotViewingConversation_PostsNotification()
    {
        FakePortService portService = new();
        RecordingNotificationService notificationService = new();
        _ = new MessageService(
            portService,
            Substitute.For<IPacketStorageService>(),
            CreateConfiguration("N0CALL"),
            Substitute.For<IAprsPortSettingsResolver>(),
            new TestPacketFactory(),
            NullLogger<MessageService>.Instance,
            SynchronousUiDispatcher.Instance,
            notificationService: notificationService);
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

        await notificationService.Shown.Task.WaitAsync(TestTimeouts.TestTimeout, TestContext.Current.CancellationToken);

        Assert.Equal(
            ("K0PEER-1", "Ping", "K0PEER-1"),
            Assert.Single(notificationService.Notifications));
    }

    [Fact]
    public void PacketReceived_ViewingConversation_SuppressesNotification()
    {
        FakePortService portService = new();
        RecordingNotificationService notificationService = new();
        ActiveConversationTracker tracker = new();
        MessageService service = new(
            portService,
            Substitute.For<IPacketStorageService>(),
            CreateConfiguration("N0CALL"),
            Substitute.For<IAprsPortSettingsResolver>(),
            new TestPacketFactory(),
            NullLogger<MessageService>.Instance,
            SynchronousUiDispatcher.Instance,
            notificationService: notificationService,
            activeConversationTracker: tracker);

        AprsAddress peer = AprsAddress.FromCallsign(new Callsign("K0PEER", 1));
        tracker.Activate(peer);

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

        // Ingestion runs synchronously through the UI dispatcher; once the message
        // is in the conversation there is nothing left that could notify.
        ConversationThread thread = Assert.Single(service.Conversations);
        Assert.Equal("Ping", Assert.Single(thread.Messages).Text);
        Assert.Empty(notificationService.Notifications);
    }

    [Fact]
    public void ClearedActiveConversation_NotifiesTheIncomingMessageAgain()
    {
        FakePortService portService = new();
        RecordingNotificationService notificationService = new();
        ActiveConversationTracker tracker = new();
        MessageService service = new(
            portService,
            Substitute.For<IPacketStorageService>(),
            CreateConfiguration("N0CALL"),
            Substitute.For<IAprsPortSettingsResolver>(),
            new TestPacketFactory(),
            NullLogger<MessageService>.Instance,
            SynchronousUiDispatcher.Instance,
            notificationService: notificationService,
            activeConversationTracker: tracker);

        AprsAddress peer = AprsAddress.FromCallsign(new Callsign("K0PEER", 1));
        tracker.Activate(peer);
        tracker.Clear(peer);

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
        Assert.Equal(
            ("K0PEER-1", "Ping", "K0PEER-1"),
            Assert.Single(notificationService.Notifications));
        Assert.Equal("Ping", Assert.Single(Assert.Single(service.Conversations).Messages).Text);
    }

    [Fact]
    public void ClearingADifferentPeer_KeepsTheVisibleConversationSuppressed()
    {
        FakePortService portService = new();
        RecordingNotificationService notificationService = new();
        ActiveConversationTracker tracker = new();
        MessageService service = new(
            portService,
            Substitute.For<IPacketStorageService>(),
            CreateConfiguration("N0CALL"),
            Substitute.For<IAprsPortSettingsResolver>(),
            new TestPacketFactory(),
            NullLogger<MessageService>.Instance,
            SynchronousUiDispatcher.Instance,
            notificationService: notificationService,
            activeConversationTracker: tracker);

        Callsign viewedCallsign = new("K0PEER", 1);
        AprsAddress viewed = AprsAddress.FromCallsign(viewedCallsign);
        Callsign otherCallsign = new("K0OTH", 2);
        AprsAddress other = AprsAddress.FromCallsign(otherCallsign);
        tracker.Activate(viewed);

        // Clearing a different peer (e.g. retiring a stale overlay) must NOT
        // clear the conversation actually being viewed.
        tracker.Clear(other);

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

        Assert.Empty(notificationService.Notifications);
        Assert.Equal("Ping", Assert.Single(Assert.Single(service.Conversations).Messages).Text);
    }



    [Fact]
    public async Task SendAsync_EnabledPortWithIsTxFalse_ThrowsNoTxPorts()
    {
        PortConfig port = new()
        {
            Id = Guid.NewGuid(),
            Name = "RX-only",
            IsEnabled = true,
            IsTx = false,
            IsRx = true,
            TypeSettings = new AprsIsSettings()
        };
        TestPacketFactory packetFactory = new();
        MessageService service = new(
            new FakePortService(port),
            Substitute.For<IPacketStorageService>(),
            CreateConfiguration("N0CALL"),
            Substitute.For<IAprsPortSettingsResolver>(),
            packetFactory,
            NullLogger<MessageService>.Instance,
            SynchronousUiDispatcher.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.SendAsync(AprsAddress.FromCallsign(new Callsign("K0OTH")), "Hello", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SendAsync_ThrowsWhenNoTxPorts()
    {
        FakePortService portService = new();
        TestPacketFactory packetFactory = new();
        MessageService service = new(
            portService,
            Substitute.For<IPacketStorageService>(),
            CreateConfiguration("N0CALL"),
            Substitute.For<IAprsPortSettingsResolver>(),
            packetFactory,
            NullLogger<MessageService>.Instance,
            SynchronousUiDispatcher.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.SendAsync(AprsAddress.FromCallsign(new Callsign("K0OTH")), "Hello", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SendAsync_ThrowsWhenTextTooLong()
    {
        PortConfig port = new()
        {
            Id = Guid.NewGuid(),
            Name = "TX",
            IsEnabled = true,
            IsTx = true,
            TypeSettings = new AprsIsSettings()
        };
        TestPacketFactory packetFactory = new();
        MessageService service = new(
            new FakePortService(port),
            Substitute.For<IPacketStorageService>(),
            CreateConfiguration("N0CALL"),
            Substitute.For<IAprsPortSettingsResolver>(),
            packetFactory,
            NullLogger<MessageService>.Instance,
            SynchronousUiDispatcher.Instance);

        string longText = new('A', 68);
        await Assert.ThrowsAsync<ArgumentException>(
            () => service.SendAsync(AprsAddress.FromCallsign(new Callsign("K0OTH")), longText, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void PacketReceived_IgnoresMessagesNotAddressedToUs()
    {
        FakePortService portService = new();
        TestPacketFactory packetFactory = new();
        MessageService service = new(
            portService,
            Substitute.For<IPacketStorageService>(),
            CreateConfiguration("N0CALL"),
            Substitute.For<IAprsPortSettingsResolver>(),
            packetFactory,
            NullLogger<MessageService>.Instance,
            SynchronousUiDispatcher.Instance);

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
        PortConfig port = new()
        {
            Id = Guid.NewGuid(),
            Name = "TX",
            IsEnabled = true,
            IsTx = true,
            IsRx = true,
            TypeSettings = new AprsIsSettings()
        };
        FakePortService portService = new(port);
        IAprsPortSettingsResolver resolver = Substitute.For<IAprsPortSettingsResolver>();
        resolver.GetCallsign("N0CALL").Returns("N0CALL-1");
        TestPacketFactory packetFactory = new();

        MessageService service = new(
            portService,
            Substitute.For<IPacketStorageService>(),
            CreateConfiguration("N0CALL", defaultSsid: 1),
            resolver,
            packetFactory,
            NullLogger<MessageService>.Instance,
            SynchronousUiDispatcher.Instance);

        AprsAddress addressee = AprsAddress.FromCallsign(new Callsign("K0OTH", 7));

        // Act - send 10 messages concurrently
        Task[] tasks = [.. Enumerable.Range(1, 10).Select(i => service.SendAsync(addressee, $"Message {i}", TestContext.Current.CancellationToken))];

        await Task.WhenAll(tasks);

        // Assert - all message numbers should be unique
        List<int?> messageNumbers = [.. portService.SentPackets
            .OfType<MessagePacket>()
            .Select(p => p.MessageNumber)];

        Assert.Equal(10, messageNumbers.Count);
        Assert.Equal(10, messageNumbers.Distinct().Count()); // All unique
    }

    [Fact]
    public async Task PacketReceived_ConcurrentPackets_CreatesConversationsThreadSafely()
    {
        // Arrange
        FakePortService portService = new();
        TestPacketFactory packetFactory = new();
        MessageService service = new(
            portService,
            Substitute.For<IPacketStorageService>(),
            CreateConfiguration("N0CALL"),
            Substitute.For<IAprsPortSettingsResolver>(),
            packetFactory,
            NullLogger<MessageService>.Instance,
            SynchronousUiDispatcher.Instance);

        // Act - receive 100 packets concurrently from different callsigns
        Task[] tasks = [.. Enumerable.Range(1, 100)
            .Select(i => Task.Run(() =>
            {
                // Generate callsigns: K1A to K100 format, ensuring 2-6 chars
                string callsign = i < 10 ? $"K{i}A" : i < 100 ? $"K{i}" : "K100";
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
            }, TestContext.Current.CancellationToken))];

        await Task.WhenAll(tasks);

        // Assert - should have 100 conversations, one per unique callsign
        Assert.Equal(100, service.Conversations.Count);

        // Verify each conversation has exactly one message
        foreach (ConversationThread conversation in service.Conversations)
        {
            Assert.Single(conversation.Messages);
        }
    }

    [Fact]
    public async Task GetOrCreateConversation_ConcurrentAccess_ReturnsSameInstanceForSameCallsign()
    {
        // Arrange
        FakePortService portService = new();
        TestPacketFactory packetFactory = new();
        MessageService service = new(
            portService,
            Substitute.For<IPacketStorageService>(),
            CreateConfiguration("N0CALL"),
            Substitute.For<IAprsPortSettingsResolver>(),
            packetFactory,
            NullLogger<MessageService>.Instance,
            SynchronousUiDispatcher.Instance);

        AprsAddress callsign = AprsAddress.FromCallsign(new Callsign("K0OTH", 7));
        ConcurrentBag<ConversationThread> conversations = [];

        // Act - call GetOrCreateConversation 100 times concurrently
        Task[] tasks = [.. Enumerable.Range(1, 100)
            .Select(_ => Task.Run(() =>
            {
                ConversationThread conversation = service.GetOrCreateConversation(callsign);
                conversations.Add(conversation);
            }, TestContext.Current.CancellationToken))];

        await Task.WhenAll(tasks);

        // Assert - should have only 1 conversation in service
        Assert.Single(service.Conversations);

        // All returned instances should be the same reference
        List<ConversationThread> distinctConversations = [.. conversations.Distinct()];
        Assert.Single(distinctConversations);
    }

    [Fact]
    public async Task MessageAck_FromPeer_MarksMessageAcknowledged()
    {
        PortConfig port = CreateTxPort();
        FakePortService portService = new(port);
        MessageService service = CreateMessageService(portService);

        Callsign peerCallsign = new("K0OTH", 7);
        AprsAddress peer = AprsAddress.FromCallsign(peerCallsign);
        await service.SendAsync(peer, "Hello", TestContext.Current.CancellationToken);

        ConversationThread thread = Assert.Single(service.Conversations);
        StoredMessage stored = Assert.Single(thread.Messages);
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
        PortConfig port = CreateTxPort();
        FakePortService portService = new(port);
        MessageService service = CreateMessageService(portService);

        AprsAddress peer = AprsAddress.FromCallsign(new Callsign("K0OTH", 7));
        await service.SendAsync(peer, "Hello", TestContext.Current.CancellationToken);

        ConversationThread thread = Assert.Single(service.Conversations);
        StoredMessage stored = Assert.Single(thread.Messages);
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
        PortConfig port = CreateTxPort();
        FakePortService portService = new(port);
        MessageService service = CreateMessageService(portService);

        AprsAddress peer = AprsAddress.FromCallsign(new Callsign("K0OTH", 7));
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

        ConversationThread thread = Assert.Single(service.Conversations);
        StoredMessage stored = Assert.Single(thread.Messages);
        Assert.Equal(MessageDeliveryStatus.Pending, stored.DeliveryStatus);
    }

    [Fact]
    public async Task MessageRej_FromPeer_MarksMessageRejected()
    {
        PortConfig port = CreateTxPort();
        FakePortService portService = new(port);
        MessageService service = CreateMessageService(portService);

        Callsign peerCallsign = new("K0OTH", 7);
        AprsAddress peer = AprsAddress.FromCallsign(peerCallsign);
        await service.SendAsync(peer, "Hello", TestContext.Current.CancellationToken);

        ConversationThread thread = Assert.Single(service.Conversations);
        StoredMessage stored = Assert.Single(thread.Messages);
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
        PortConfig port = CreateTxPort();
        FakePortService portService = new(port);
        IConfigurationService configuration = Substitute.For<IConfigurationService>();
        configuration.Current.Returns(new AppSettings
        {
            Aprs = new AprsSettings
            {
                Callsign = "N0CALL",
                AutoAcknowledgeMessages = true
            }
        });
        IAprsPortSettingsResolver resolver = Substitute.For<IAprsPortSettingsResolver>();
        resolver.GetCallsign("N0CALL").Returns("N0CALL-1");
        _ = new MessageService(
            portService,
            Substitute.For<IPacketStorageService>(),
            configuration,
            resolver,
            new TestPacketFactory(),
            NullLogger<MessageService>.Instance,
            SynchronousUiDispatcher.Instance);

        Callsign peerCallsign = new("K0OTH", 7);
        AprsAddress peer = AprsAddress.FromCallsign(peerCallsign);
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

        MessageAckPacket ack = Assert.IsType<MessageAckPacket>(
            await portService.PacketSent.Task.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken));
        Assert.Equal(peer, ack.Addressee);
        Assert.Equal(5, ack.MessageNumber);
        Assert.Equal(new Callsign("N0CALL", 1), ack.Source);
    }

    [Fact]
    public async Task ProcessRetries_DueMessage_ResendsWithIncrementedRetryCount()
    {
        PortConfig port = CreateTxPort();
        FakePortService portService = new(port);
        TestClock clock = new();
        MessageService service = CreateMessageService(portService, timeProvider: clock);

        AprsAddress peer = AprsAddress.FromCallsign(new Callsign("K0OTH", 7));
        await service.SendAsync(peer, "Hello", TestContext.Current.CancellationToken);

        ConversationThread thread = Assert.Single(service.Conversations);
        StoredMessage stored = Assert.Single(thread.Messages);
        Assert.Equal(0, stored.RetryCount);
        Assert.Equal(clock.UtcNow.AddSeconds(RetryTimeoutSeconds), stored.NextRetryTime);

        // The retry timer runs on the injected clock: advancing one interval lands
        // exactly on the due tick and drives a single retry pass.
        clock.Advance(TimeSpan.FromSeconds(RetryTimeoutSeconds));
        await portService.WaitForSendCountAsync(2, TestContext.Current.CancellationToken);

        MessagePacket resent = Assert.IsType<MessagePacket>(portService.SentPackets[1]);
        Assert.Equal(stored.MessageNumber, resent.MessageNumber);
        Assert.Equal(1, stored.RetryCount);
        Assert.Equal(clock.UtcNow.AddSeconds(RetryTimeoutSeconds), stored.NextRetryTime);
        Assert.Equal(1, service.PendingOutboundCount);
    }

    [Fact]
    public async Task ProcessRetries_RetriesExhausted_MarksMessageTimeout()
    {
        PortConfig port = CreateTxPort();
        FakePortService portService = new(port);
        TestClock clock = new();
        MessageService service = CreateMessageService(portService, timeProvider: clock);

        AprsAddress peer = AprsAddress.FromCallsign(new Callsign("K0OTH", 7));
        await service.SendAsync(peer, "Hello", TestContext.Current.CancellationToken);

        ConversationThread thread = Assert.Single(service.Conversations);
        StoredMessage stored = Assert.Single(thread.Messages);

        // Walk the whole backoff schedule in virtual time: the initial attempt, the
        // bounded retries and then the tick that expires the message.
        clock.Advance(TimeSpan.FromSeconds(ExpirySeconds));
        await portService.WaitForSendCountAsync(1 + MaxRetries, TestContext.Current.CancellationToken);

        Assert.Equal(MessageDeliveryStatus.Timeout, stored.DeliveryStatus);
        Assert.Equal(MaxRetries, stored.RetryCount);
        Assert.Equal(1 + MaxRetries, portService.SentPackets.Count);

        // Expiry removes the message from the retry queue, so nothing can resend it.
        Assert.Equal(0, service.PendingOutboundCount);
        clock.Advance(TimeSpan.FromHours(1));
        Assert.Equal(1 + MaxRetries, portService.SentPackets.Count);
    }

    [Fact]
    public async Task RetryingMessage_StoresOneOutboundRecordPerLogicalMessage()
    {
        PortConfig port = CreateTxPort();
        FakePortService portService = new(port);
        using TempAppDatabase db = TempAppDatabase.Create();
        IConfigurationService storageConfiguration = Substitute.For<IConfigurationService>();
        storageConfiguration.Current.Returns(new AppSettings
        {
            Aprs = new AprsSettings { Callsign = "N0CALL", Ssid = (AprsSsid)1 }
        });
        await using SqliteRuntimeWriter writer = new(db.Factory);
        PacketStorageService storage = new(
            writer,
            storageConfiguration,
            NullLogger<PacketStorageService>.Instance);
        TestClock clock = new();
        MessageService service = CreateMessageService(portService, db.Factory, storage, clock);

        AprsAddress peer = AprsAddress.FromCallsign(new Callsign("K0OTH", 7));
        await service.SendAsync(peer, "Hello", TestContext.Current.CancellationToken);

        ConversationThread thread = Assert.Single(service.Conversations);
        StoredMessage stored = Assert.Single(thread.Messages);

        // One initial attempt plus five retries, then expiry — all on virtual time.
        clock.Advance(TimeSpan.FromSeconds(ExpirySeconds));
        await portService.WaitForSendCountAsync(1 + MaxRetries, TestContext.Current.CancellationToken);

        Assert.Equal(MessageDeliveryStatus.Timeout, stored.DeliveryStatus);
        Assert.Equal(MaxRetries, stored.RetryCount);

        Assert.Equal(1 + MaxRetries, portService.SentPackets.Count);

        // Status persistence runs on the thread pool; the flush waits for every
        // queued status write instead of polling the database.
        await service.FlushAsync(TestContext.Current.CancellationToken);

        int outboundRows = await CountOutboundMessageRowsAsync(db, stored.MessageNumber);
        Assert.Equal(1, outboundRows);

        await using AppDbContext verification = db.CreateContext();
        PacketRecord record = await verification.Packets
            .Include(p => p.Message)
            .SingleAsync(
                p => p.IsOutbound && p.Message != null && p.Message.Number == stored.MessageNumber,
                TestContext.Current.CancellationToken);
        Assert.Equal(MessageDeliveryStatus.Timeout, record.Message!.DeliveryStatus);
        Assert.Equal(5, record.Message.RetryCount);
    }

    private static async Task<int> CountOutboundMessageRowsAsync(
        TempAppDatabase db,
        int? messageNumber)
    {
        await using AppDbContext context = db.CreateContext();
        return await context.Packets
            .Where(p => p.IsOutbound && p.Message != null && p.Message.Number == messageNumber)
            .CountAsync(TestContext.Current.CancellationToken);
    }

    private static PortConfig CreateTxPort() => new()
    {
        Id = Guid.NewGuid(),
        Name = "TX",
        IsEnabled = true,
        IsTx = true,
        IsRx = true,
        TypeSettings = new AprsIsSettings()
    };

    private static MessageService CreateMessageService(
        FakePortService portService,
        IDbContextFactory<AppDbContext>? dbContextFactory = null,
        IPacketStorageService? packetStorageService = null,
        TimeProvider? timeProvider = null,
        ILogger<MessageService>? logger = null)
    {
        IConfigurationService configuration = Substitute.For<IConfigurationService>();
        configuration.Current.Returns(new AppSettings
        {
            Aprs = new AprsSettings
            {
                Callsign = "N0CALL",
                Ssid = (AprsSsid)1
            }
        });
        IAprsPortSettingsResolver resolver = Substitute.For<IAprsPortSettingsResolver>();
        resolver.GetCallsign("N0CALL").Returns("N0CALL-1");

        return new MessageService(
            portService,
            packetStorageService ?? Substitute.For<IPacketStorageService>(),
            configuration,
            resolver,
            new TestPacketFactory(),
            logger ?? NullLogger<MessageService>.Instance,
            SynchronousUiDispatcher.Instance,
            dbContextFactory is null
                ? null
                : new PacketQueryService(new SqliteRuntimeReader(dbContextFactory), NullLogger<PacketQueryService>.Instance),
            timeProvider: timeProvider);
    }

    private static IConfigurationService CreateConfiguration(string callsign, int defaultSsid = 0)
    {
        IConfigurationService configuration = Substitute.For<IConfigurationService>();
        configuration.Current.Returns(new AppSettings
        {
            Aprs = new AprsSettings
            {
                Callsign = callsign,
                Ssid = (AprsSsid)defaultSsid
            }
        });
        return configuration;
    }

    private sealed class RecordingNotificationService : INotificationService
    {
        private readonly Lock _sync = new();

        public List<(string Callsign, string Text, string ConversationId)> Notifications { get; } = [];

        public TaskCompletionSource Shown { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task ShowMessageNotificationAsync(
            string callsign,
            string text,
            string conversationId,
            CancellationToken cancellationToken = default)
        {
            lock (_sync)
            {
                Notifications.Add((callsign, text, conversationId));
            }

            Shown.TrySetResult();
            return Task.CompletedTask;
        }
    }

    private sealed class FakePortService(params PortConfig[] ports) : IPortService
    {
        private readonly List<PortConfig> _ports = [.. ports];
        private readonly Lock _sentLock = new();
        private readonly Dictionary<int, TaskCompletionSource> _sendGates = [];

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

        /// <summary>
        /// Completes once at least <paramref name="count"/> packets have been sent,
        /// so retry tests can await the effect instead of sleeping.
        /// </summary>
        public Task WaitForSendCountAsync(int count, CancellationToken cancellationToken = default)
        {
            lock (_sentLock)
            {
                if (SentPackets.Count >= count)
                {
                    return Task.CompletedTask;
                }

                if (!_sendGates.TryGetValue(count, out TaskCompletionSource? gate))
                {
                    gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    _sendGates[count] = gate;
                }

                return gate.Task.WaitAsync(cancellationToken);
            }
        }

        public void RaisePacketReceived(PortPacketReceivedEventArgs args) =>
            PacketReceived?.Invoke(this, args);

        public Task SendPacketAsync(Guid id, AprsPacket packet)
        {
            if (FailSends)
            {
                throw new InvalidOperationException("simulated transport loss");
            }

            lock (_sentLock)
            {
                SentPackets.Add(packet);
                if (_sendGates.TryGetValue(SentPackets.Count, out TaskCompletionSource? gate))
                {
                    gate.TrySetResult();
                }
            }

            PacketSent.TrySetResult(packet);
            return Task.CompletedTask;
        }

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
            string[] parts = callsign.Split('-');
            Callsign source = parts.Length > 1
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
            string[] parts = sourceCallsign.Split('-');
            Callsign source = parts.Length > 1
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
            string[] parts = sourceCallsign.Split('-');
            Callsign source = parts.Length > 1
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
            string[] parts = sourceCallsign.Split('-');
            Callsign source = parts.Length > 1
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
            string[] parts = sourceCallsign.Split('-');
            Callsign source = parts.Length > 1
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
            string[] parts = sourceCallsign.Split('-');
            Callsign source = parts.Length > 1
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
            string[] parts = sourceCallsign.Split('-');
            Callsign source = parts.Length > 1
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
