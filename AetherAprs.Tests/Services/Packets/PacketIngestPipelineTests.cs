// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Models.Aprs;
using AetherAprs.Models.Aprs.Packets;
using AetherAprs.Services.Contracts;
using AetherAprs.Services.Packets;
using AetherAprs.Tests.Helpers;
using Geo;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace AetherAprs.Tests.Services.Packets;

public sealed class PacketIngestPipelineTests
{
    [Fact]
    public async Task FullQueue_DropsTheNewestPacketAndCountsIt()
    {
        var portId = Guid.NewGuid();
        var storage = new GatedPacketStorage { GateFirstStore = true };
        await using var pipeline = CreatePipeline(
            storage,
            new PacketIngestOptions { QueueCapacity = 1 });

        pipeline.Accept(portId, CreatePacket());

        // The consumer holds the first packet, so the queue slot is filled by the
        // second and the third has nowhere to go.
        await storage.FirstStoreEntered.WaitAsync(
            TestTimeouts.TestTimeout,
            TestContext.Current.CancellationToken);
        pipeline.Accept(portId, CreatePacket());
        pipeline.Accept(portId, CreatePacket());

        Assert.Equal(1, pipeline.GetDroppedPacketCount(portId));
        Assert.Equal(1, pipeline.TotalDroppedPackets);
        Assert.Equal(2, pipeline.GetMetrics().Enqueued);

        storage.ReleaseFirstStore();
        await WaitForAsync(
            () => pipeline.GetMetrics().Stored == 2,
            "Every queued packet must still be stored.");
    }

    [Fact]
    public async Task PacketStored_IsRaisedOnlyAfterThePacketIsDurable()
    {
        var portId = Guid.NewGuid();
        var storage = new GatedPacketStorage { GateFirstStore = true };
        await using var pipeline = CreatePipeline(storage);
        var publishedBeforeStorage = false;
        var published = new TaskCompletionSource<PortPacketReceivedEventArgs>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        pipeline.PacketStored += (_, args) =>
        {
            publishedBeforeStorage |= !storage.StoredPackets.Contains(args.Packet);
            published.TrySetResult(args);
        };

        var packet = CreatePacket();
        pipeline.Accept(portId, packet);
        await storage.FirstStoreEntered.WaitAsync(
            TestTimeouts.TestTimeout,
            TestContext.Current.CancellationToken);

        Assert.False(published.Task.IsCompleted);
        storage.ReleaseFirstStore();

        var args = await published.Task.WaitAsync(
            TestTimeouts.TestTimeout,
            TestContext.Current.CancellationToken);
        Assert.Same(packet, args.Packet);
        Assert.Equal(portId, args.PortId);
        Assert.False(publishedBeforeStorage);
    }

    [Fact]
    public async Task StorageFailure_DropsThePacketWithoutPublishingIt()
    {
        var portId = Guid.NewGuid();
        var storage = new GatedPacketStorage
        {
            Failure = new InvalidOperationException("storage down")
        };
        await using var pipeline = CreatePipeline(storage);
        var published = 0;
        pipeline.PacketStored += (_, _) => Interlocked.Increment(ref published);

        pipeline.Accept(portId, CreatePacket());

        await WaitForAsync(
            () => pipeline.GetDroppedPacketCount(portId) == 1,
            "An unstored packet must be counted as dropped.");
        Assert.Equal(0, published);
        Assert.Equal(0, pipeline.GetMetrics().Stored);
    }

    [Fact]
    public void Constructor_RejectsInvalidOptions()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CreatePipeline(new GatedPacketStorage(), new PacketIngestOptions { QueueCapacity = 0 }));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CreatePipeline(new GatedPacketStorage(), new PacketIngestOptions { StoreAttemptLimit = 0 }));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CreatePipeline(
                new GatedPacketStorage(),
                new PacketIngestOptions { StoreRetryDelay = TimeSpan.FromSeconds(-1) }));
    }

    [Fact]
    public async Task Accept_BeforeStart_IsStoredOnceTheConsumerStarts()
    {
        var storage = new GatedPacketStorage();
        await using var pipeline = new PacketIngestPipeline(
            storage,
            NullLogger<PacketIngestPipeline>.Instance,
            options: new PacketIngestOptions { QueueCapacity = 4 });

        pipeline.Accept(Guid.NewGuid(), CreatePacket());
        await Task.Delay(50, TestContext.Current.CancellationToken);

        // Construction only wires the queue; the startup phase starts the drain.
        Assert.Empty(storage.StoredPackets);

        await pipeline.StartAsync(TestContext.Current.CancellationToken);
        await WaitForAsync(
            () => storage.StoredPackets.Count == 1,
            "The queued packet should be stored once the consumer starts.");
    }

    private static PacketIngestPipeline CreatePipeline(
        IPacketStorageService storage,
        PacketIngestOptions? options = null)
    {
        var pipeline = new PacketIngestPipeline(
            storage,
            NullLogger<PacketIngestPipeline>.Instance,
            options: options);

        // The consumer is a startup phase, so the helper starts it explicitly.
        // StartAsync completes synchronously.
        pipeline.StartAsync().GetAwaiter().GetResult();
        return pipeline;
    }

    private static async Task WaitForAsync(Func<bool> condition, string because)
    {
        var deadline = DateTime.UtcNow + TestTimeouts.TestTimeout;
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
            {
                Assert.Fail(because);
            }

            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }

    private static PositionPacket CreatePacket() => new()
    {
        Source = new Callsign("N0CALL", 1),
        Destination = new Callsign("APRS"),
        Location = new Coordinate(0, 0)
    };

    private sealed class GatedPacketStorage : IPacketStorageService
    {
#pragma warning disable CS0067 // The pipeline observes its own stored event.
        public event EventHandler? PacketStored;
#pragma warning restore CS0067

        private readonly TaskCompletionSource _firstStoreEntered =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _releaseFirstStore =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool GateFirstStore { get; init; }

        public Exception? Failure { get; init; }

        public List<AprsPacket> StoredPackets { get; } = [];

        public int StoreAttempts { get; private set; }

        public Task FirstStoreEntered => _firstStoreEntered.Task;

        public void ReleaseFirstStore() => _releaseFirstStore.TrySetResult();

        public async Task StorePacketAsync(
            AprsPacket packet,
            Guid? portId,
            bool isOutbound = false,
            CancellationToken cancellationToken = default)
        {
            StoreAttempts++;
            if (GateFirstStore && StoreAttempts == 1)
            {
                _firstStoreEntered.TrySetResult();
                await _releaseFirstStore.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }

            if (Failure is not null)
            {
                throw Failure;
            }

            StoredPackets.Add(packet);
        }

        public Task EnsureOutboundMessageAsync(
            MessagePacket packet,
            Guid? portId,
            int retryCount,
            DateTimeOffset? nextRetryTime,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task UpdateOutboundMessageStatusAsync(
            AprsAddress addressee,
            int messageNumber,
            AetherAprs.Models.Messaging.MessageDeliveryStatus deliveryStatus,
            int retryCount,
            DateTimeOffset? nextRetryTime,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task CleanupOldPacketsAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
