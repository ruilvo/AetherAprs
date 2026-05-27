// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

namespace AetherAprs.Tests;

using AetherAprs.Messages;
using AetherAprs.Models;
using AetherAprs.Protocols;
using AetherAprs.Services.Aprs;
using AetherAprs.Services.Configuration;
using AetherAprs.Services.Logging;
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
            Logging = new LoggingSettings
            {
                LogLevel = AetherAprs.Services.Logging.LogLevel.Information,
                WriteToFile = false,
            },
            AprsIs = new AprsSettings
            {
                Host = "rotate.aprs2.net",
                Port = 14580,
                Callsign = "N0CALL-1",
                Passcode = "12345",
                Filter = "r/38.7/-9.1/50",
            },
        };

        var clone = settings.Clone();

        Assert.Equal(settings.AprsIs.Host, clone.AprsIs.Host);
        Assert.Equal(settings.AprsIs.Port, clone.AprsIs.Port);
        Assert.Equal(settings.AprsIs.Callsign, clone.AprsIs.Callsign);
        Assert.Equal(settings.AprsIs.Passcode, clone.AprsIs.Passcode);
        Assert.Equal(settings.AprsIs.Filter, clone.AprsIs.Filter);
    }

    [Fact]
    public async Task Session_service_broadcasts_outbound_message_to_all_backends()
    {
        var first = new FakeAprsBackend();
        var second = new FakeAprsBackend();
        await using var session = new AprsSessionService(new NullLoggingService());
        var packet = CreatePacket();

        await session.RegisterBackendAsync(first);
        await session.RegisterBackendAsync(second);
        WeakReferenceMessenger.Default.Send(new SendAprsPacketMessage(packet));

        await WaitForAsync(() => first.SentPackets.Count == 1 && second.SentPackets.Count == 1);

        Assert.True(first.ConnectCalled);
        Assert.True(second.ConnectCalled);
        Assert.Same(packet, first.SentPackets.Single());
        Assert.Same(packet, second.SentPackets.Single());
    }

    [Fact]
    public async Task Session_service_publishes_received_packets()
    {
        var backend = new FakeAprsBackend();
        await using var session = new AprsSessionService(new NullLoggingService());
        var recipient = new PacketRecipient();
        var packet = CreatePacket();

        WeakReferenceMessenger.Default.Register<PacketRecipient, AprsPacketReceivedMessage>(recipient, static (r, m) => r.Packet = m.Value);

        await session.RegisterBackendAsync(backend);
        await backend.PublishAsync(packet);
        await backend.CompleteAsync();

        await WaitForAsync(() => recipient.Packet is not null);

        Assert.Same(packet, recipient.Packet);
        WeakReferenceMessenger.Default.UnregisterAll(recipient);
    }

    [Fact]
    public async Task Unregistering_backend_stops_delivery_and_disposes_async_backend()
    {
        var first = new FakeAprsBackend();
        var second = new FakeAprsBackend();
        await using var session = new AprsSessionService(new NullLoggingService());

        var firstHandle = await session.RegisterBackendAsync(first);
        await session.RegisterBackendAsync(second);

        await session.UnregisterBackendAsync(firstHandle);

        WeakReferenceMessenger.Default.Send(new SendAprsPacketMessage(CreatePacket()));
        await WaitForAsync(() => second.SentPackets.Count == 1);

        Assert.True(first.Disposed);
        Assert.Empty(first.SentPackets);
        Assert.Single(second.SentPackets);
    }

    private static async Task WaitForAsync(System.Func<bool> predicate)
    {
        for (var attempt = 0; attempt < 50 && !predicate(); attempt++)
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

    private sealed class NullLoggingService : ILoggingService
    {
        public void Log(LogLevel level, string message) { }
        public void Debug(string message) { }
        public void Info(string message) { }
        public void Warn(string message) { }
        public void Error(string message) { }
        public void Critical(string message) { }
        public ILoggingService ForContext(string contextName) => this;
    }

    private sealed class FakeAprsBackend : IAprsBackend, System.IAsyncDisposable
    {
        private readonly Channel<AprsPacket> _receivedPackets = Channel.CreateUnbounded<AprsPacket>();

        public bool ConnectCalled { get; private set; }

        public bool Disposed { get; private set; }

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

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            _receivedPackets.Writer.TryComplete();
            return ValueTask.CompletedTask;
        }
    }
}
