// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Data.Transmission;
using AetherAprs.Models;
using AetherAprs.Services.Transmission;
using AetherAprs.Tests.Helpers;
using Geo;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace AetherAprs.Tests.Services.Transmission;

public sealed class BeaconOutboxStoreTests
{
    [Fact]
    public async Task Reconcile_ReturnsInterruptedSendToQueuedState()
    {
        using TempAppDatabase database = TempAppDatabase.Create();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        IBeaconTransmitQueue queue = StoreFactory.CreateBeaconOutbox(database);
        BeaconQueueItem item = (await queue.EnqueueAsync(
            Guid.NewGuid(), Location(now), now, TestContext.Current.CancellationToken)).Queued!;
        await queue.MarkSendingAsync(item.Id, TestContext.Current.CancellationToken);

        // A second store over the same database stands in for the restarted
        // process: the "in flight" row is genuinely persisted, not in memory.
        IBeaconTransmitQueue recovered = StoreFactory.CreateBeaconOutbox(database);

        BeaconQueueItem pending = Assert.Single(
            await recovered.ReconcileAsync(now.AddMinutes(1), TestContext.Current.CancellationToken));
        Assert.Equal(item.Id, pending.Id);
        Assert.Equal(BeaconQueueStatus.Queued, pending.Status);
        Assert.Equal(BeaconQueueStatus.Queued, Assert.Single(
            await recovered.GetSnapshotAsync(TestContext.Current.CancellationToken)).Status);
    }

    [Fact]
    public async Task Reconcile_ExpiresStaleBeaconWithTerminalStatus()
    {
        using TempAppDatabase database = TempAppDatabase.Create();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        IBeaconTransmitQueue queue = StoreFactory.CreateBeaconOutbox(database);
        BeaconQueueItem item = (await queue.EnqueueAsync(
            Guid.NewGuid(), Location(now), now, TestContext.Current.CancellationToken)).Queued!;

        IReadOnlyList<BeaconQueueItem> due = await queue.ReconcileAsync(
            now.Add(BeaconOutboxStore.DefaultExpiry).AddSeconds(1),
            TestContext.Current.CancellationToken);

        Assert.Empty(due);
        BeaconQueueItem expired = Assert.Single(
            await queue.GetSnapshotAsync(TestContext.Current.CancellationToken));
        Assert.Equal(item.Id, expired.Id);
        Assert.Equal(BeaconQueueStatus.Expired, expired.Status);
    }

    [Fact]
    public async Task FailedAttempts_BecomeTerminalAfterBoundedRetryCount()
    {
        using TempAppDatabase database = TempAppDatabase.Create();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        IBeaconTransmitQueue queue = StoreFactory.CreateBeaconOutbox(database);
        BeaconQueueItem item = (await queue.EnqueueAsync(
            Guid.NewGuid(), Location(now), now, TestContext.Current.CancellationToken)).Queued!;

        for (int attempt = 0; attempt < BeaconOutboxStore.MaximumAttempts; attempt++)
        {
            await queue.MarkAttemptFailedAsync(
                item.Id, "offline", now, TestContext.Current.CancellationToken);
        }

        BeaconQueueItem failed = Assert.Single(
            await queue.GetSnapshotAsync(TestContext.Current.CancellationToken));
        Assert.Equal(BeaconQueueStatus.Failed, failed.Status);
        Assert.Equal(BeaconOutboxStore.MaximumAttempts, failed.AttemptCount);
        Assert.Equal("offline", failed.Error);
        Assert.Empty(await queue.ReconcileAsync(now, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task MarkSent_KeepsBeaconOutOfTheQueue()
    {
        using TempAppDatabase database = TempAppDatabase.Create();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        IBeaconTransmitQueue queue = StoreFactory.CreateBeaconOutbox(database);
        BeaconQueueItem item = (await queue.EnqueueAsync(
            Guid.NewGuid(), Location(now), now, TestContext.Current.CancellationToken)).Queued!;

        await queue.MarkSendingAsync(item.Id, TestContext.Current.CancellationToken);
        await queue.MarkSentAsync(item.Id, TestContext.Current.CancellationToken);

        // Reconciliation must not resurrect a beacon that reached the air.
        Assert.Empty(await queue.ReconcileAsync(now.AddMinutes(1), TestContext.Current.CancellationToken));
        Assert.Equal(BeaconQueueStatus.Sent, Assert.Single(
            await queue.GetSnapshotAsync(TestContext.Current.CancellationToken)).Status);
    }

    [Fact]
    public async Task Enqueue_WhenFull_EvictsOldestQueuedBeaconWithoutThrowing()
    {
        using TempAppDatabase database = TempAppDatabase.Create();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        IBeaconTransmitQueue queue = StoreFactory.CreateBeaconOutbox(database, maximumItems: 1);

        BeaconEnqueueResult first = await queue.EnqueueAsync(
            Guid.NewGuid(), Location(now), now, TestContext.Current.CancellationToken);
        Assert.Equal(BeaconQueueStatus.Queued, first.Status);
        Assert.NotNull(first.Queued);
        Assert.Null(first.Dropped);

        BeaconEnqueueResult second = await queue.EnqueueAsync(
            Guid.NewGuid(), Location(now), now, TestContext.Current.CancellationToken);
        Assert.Equal(BeaconQueueStatus.Queued, second.Status);
        Assert.NotNull(second.Queued);
        Assert.NotNull(second.Dropped);
        Assert.Equal(first.Queued!.Id, second.Dropped!.Id);
        Assert.Equal(BeaconQueueStatus.Dropped, second.Dropped.Status);
        Assert.Equal("Queue capacity reached.", second.Dropped.Error);

        IReadOnlyList<BeaconQueueItem> snapshot =
            await queue.GetSnapshotAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, snapshot.Count);
        Assert.Contains(snapshot, entry =>
            entry.Id == second.Dropped!.Id && entry.Status == BeaconQueueStatus.Dropped);
        Assert.Contains(snapshot, entry =>
            entry.Id == second.Queued!.Id && entry.Status == BeaconQueueStatus.Queued);
    }

    [Fact]
    public async Task Enqueue_WhenEverySlotIsInFlight_RejectsWithoutEvicting()
    {
        using TempAppDatabase database = TempAppDatabase.Create();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        IBeaconTransmitQueue queue = StoreFactory.CreateBeaconOutbox(database, maximumItems: 1);
        BeaconQueueItem inFlight = (await queue.EnqueueAsync(
            Guid.NewGuid(), Location(now), now, TestContext.Current.CancellationToken)).Queued!;
        await queue.MarkSendingAsync(inFlight.Id, TestContext.Current.CancellationToken);

        BeaconEnqueueResult rejected = await queue.EnqueueAsync(
            Guid.NewGuid(), Location(now), now, TestContext.Current.CancellationToken);

        // Evicting a beacon that may already be on the air would corrupt the
        // outbox, so the newer beacon is rejected instead.
        Assert.Null(rejected.Queued);
        Assert.Null(rejected.Dropped);
        Assert.Equal(BeaconQueueStatus.Dropped, rejected.Status);
        Assert.Equal(inFlight.Id, Assert.Single(
            await queue.GetSnapshotAsync(TestContext.Current.CancellationToken)).Id);
    }

    [Fact]
    public async Task Enqueue_WithEmptyPortId_WaitsForAnyUsableRoute()
    {
        using TempAppDatabase database = TempAppDatabase.Create();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        IBeaconTransmitQueue queue = StoreFactory.CreateBeaconOutbox(database);

        BeaconQueueItem item = (await queue.EnqueueAsync(
            Guid.Empty, Location(now), now, TestContext.Current.CancellationToken)).Queued!;

        Assert.Equal(Guid.Empty, item.PortId);
        Assert.Single(await queue.ReconcileAsync(now, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Enqueue_PreservesTheCapturedPosition()
    {
        using TempAppDatabase database = TempAppDatabase.Create();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        IBeaconTransmitQueue queue = StoreFactory.CreateBeaconOutbox(database);
        LocationData captured = new()
        {
            Location = new Coordinate(38.5, -9.1),
            Altitude = 120,
            Timestamp = now
        };

        BeaconQueueItem item = (await queue.EnqueueAsync(
            Guid.NewGuid(), captured, now, TestContext.Current.CancellationToken)).Queued!;

        // A beacon queued offline must transmit the fix the operator saw, never a
        // newer one picked up while it waited.
        BeaconQueueItem stored = Assert.Single(
            await queue.GetSnapshotAsync(TestContext.Current.CancellationToken));
        Assert.Equal(item.Id, stored.Id);
        Assert.Equal(38.5, stored.Latitude, precision: 6);
        Assert.Equal(-9.1, stored.Longitude, precision: 6);
        Assert.Equal(120, stored.Altitude);
        Assert.Equal(now, stored.CreatedUtc);
    }

    [Fact]
    public async Task TerminalRows_ArePrunedAfterRetention()
    {
        using TempAppDatabase database = TempAppDatabase.Create();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        IBeaconTransmitQueue queue = StoreFactory.CreateBeaconOutbox(database);
        BeaconQueueItem item = (await queue.EnqueueAsync(
            Guid.NewGuid(), Location(now), now, TestContext.Current.CancellationToken)).Queued!;
        await queue.MarkSentAsync(item.Id, TestContext.Current.CancellationToken);

        await queue.ReconcileAsync(
            now.Add(BeaconOutboxStore.TerminalRetention).AddHours(1),
            TestContext.Current.CancellationToken);

        Assert.Empty(await queue.GetSnapshotAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task QueuedBeacon_SurvivesANewStoreInstance()
    {
        using TempAppDatabase database = TempAppDatabase.Create();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        BeaconQueueItem item = (await StoreFactory.CreateBeaconOutbox(database).EnqueueAsync(
            Guid.NewGuid(), Location(now), now, TestContext.Current.CancellationToken)).Queued!;

        IBeaconTransmitQueue restarted = StoreFactory.CreateBeaconOutbox(database);

        IReadOnlyList<BeaconQueueItem> due =
            await restarted.ReconcileAsync(now.AddSeconds(1), TestContext.Current.CancellationToken);
        BeaconQueueItem pending = Assert.Single(due);
        Assert.Equal(item.Id, pending.Id);
        Assert.Equal(BeaconQueueStatus.Queued, pending.Status);
    }

    private static LocationData Location(DateTimeOffset timestamp) =>
        new()
        {
            Location = new Coordinate(38.5, -9.1),
            Altitude = 12,
            Timestamp = timestamp
        };
}
