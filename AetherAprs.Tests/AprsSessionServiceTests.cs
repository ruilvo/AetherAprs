// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

namespace AetherAprs.Tests;

using AetherAprs.Messages;
using AetherAprs.Models;
using AetherAprs.Protocols;
using AetherAprs.Services.Aprs;
using AetherAprs.Services.Configuration;
using CommunityToolkit.Mvvm.Messaging;
using NetTopologySuite.Geometries;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Xunit;

public sealed class AprsSessionServiceTests
{
    [Fact]
    public void App_settings_clone_preserves_aprs_is_fields()
    {
        var settings = new AppSettings
        {
            Callsign = "N0CALL-1",
            Passcode = "12345",
            Filter = "r/38.7/-9.1/50",
        };

        var clone = settings.Clone();

        Assert.Equal(settings.Callsign, clone.Callsign);
        Assert.Equal(settings.Passcode, clone.Passcode);
        Assert.Equal(settings.Filter, clone.Filter);
    }

    [Fact]
    public async Task Session_service_sends_outbound_message_to_backend()
    {
        var backend = new FakeAprsBackend();
        using var session = new AprsSessionService(backend, new FakeConfigurationService());
        var packet = CreatePacket();

        await session.StartAsync();
        WeakReferenceMessenger.Default.Send(new SendAprsPacketMessage(packet));

        Assert.True(backend.ConnectCalled);
        Assert.Same(packet, backend.SentPackets.Single());
    }

    [Fact]
    public async Task Session_service_publishes_received_packets()
    {
        var backend = new FakeAprsBackend();
        using var session = new AprsSessionService(backend, new FakeConfigurationService());
        var recipient = new PacketRecipient();
        var packet = CreatePacket();

        WeakReferenceMessenger.Default.Register<PacketRecipient, AprsPacketReceivedMessage>(recipient, static (r, m) => r.Packet = m.Value);

        await session.StartAsync();
        await backend.PublishAsync(packet);
        await backend.CompleteAsync();

        await WaitForPacketAsync(recipient);

        Assert.Same(packet, recipient.Packet);
        WeakReferenceMessenger.Default.UnregisterAll(recipient);
    }

    [Fact]
    public async Task Session_service_requires_callsign_and_passcode_before_connecting()
    {
        var backend = new FakeAprsBackend();
        using var session = new AprsSessionService(backend, new FakeConfigurationService(new AppSettings()));
        var recipient = new ErrorRecipient();

        WeakReferenceMessenger.Default.Register<ErrorRecipient, AprsConnectionErrorMessage>(recipient, static (r, m) => r.Message = m.Value);

        await session.StartAsync();

        Assert.False(backend.ConnectCalled);
        Assert.Contains("Callsign", recipient.Message);
        WeakReferenceMessenger.Default.UnregisterAll(recipient);
    }

    private static async Task WaitForPacketAsync(PacketRecipient recipient)
    {
        for (var attempt = 0; attempt < 50 && recipient.Packet is null; attempt++)
        {
            await Task.Delay(10);
        }
    }

    private static AprsPacket CreatePacket() => new()
    {
        Source = "N0CALL-1",
        Destination = "APRS",
        Payload = new AprsEntry
        {
            Kind = AprsEntryKind.Station,
            Location = new Point(-9.1393, 38.7223),
            Symbol = new AprsSymbol { Table = AprsSymbolTable.Primary, Code = '>' },
            StationDataType = AprsStationDataType.PositionWithoutMessaging,
        },
    };

    private sealed class PacketRecipient
    {
        public AprsPacket? Packet { get; set; }
    }

    private sealed class ErrorRecipient
    {
        public string? Message { get; set; }
    }

    private sealed class FakeConfigurationService(AppSettings? settings = null) : IConfigurationService
    {
        public AppSettings Settings { get; private set; } = settings ?? new AppSettings
        {
            Callsign = "N0CALL-1",
            Passcode = "12345",
        };

        public bool SaveSettings(AppSettings newSettings)
        {
            Settings = newSettings;
            return true;
        }
    }

    private sealed class FakeAprsBackend : IAprsBackend
    {
        private readonly Channel<AprsPacket> _receivedPackets = Channel.CreateUnbounded<AprsPacket>();

        public bool ConnectCalled { get; private set; }

        public List<AprsPacket> SentPackets { get; } = [];

        public Task ConnectAsync(CancellationToken cancellationToken = default)
        {
            ConnectCalled = true;
            return Task.CompletedTask;
        }

        public async IAsyncEnumerable<AprsPacket> ReceiveAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await foreach (var packet in _receivedPackets.Reader.ReadAllAsync(cancellationToken))
            {
                yield return packet;
            }
        }

        public ValueTask SendAsync(AprsPacket packet, CancellationToken cancellationToken = default)
        {
            SentPackets.Add(packet);
            return ValueTask.CompletedTask;
        }

        public ValueTask PublishAsync(AprsPacket packet) => _receivedPackets.Writer.WriteAsync(packet);

        public Task CompleteAsync()
        {
            _receivedPackets.Writer.Complete();
            return Task.CompletedTask;
        }
    }
}
