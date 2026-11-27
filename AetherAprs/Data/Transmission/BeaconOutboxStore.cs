// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Data.Entities;
using AetherAprs.Models;
using AetherAprs.Services.Transmission;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AetherAprs.Data.Transmission;

/// <summary>
/// Durable beacon outbox behind the serialized runtime writer and the read-only
/// query reader.
/// </summary>
/// <remarks>
/// Every transition is a committed row change, so a beacon that was queued before a
/// crash is still there afterwards. Reconciliation is part of the store instead of
/// the constructor: it runs in one write transaction that returns interrupted sends
/// to the queue and expires anything too old to be worth transmitting.
/// </remarks>
internal sealed class BeaconOutboxStore(
    SqliteRuntimeWriter writer,
    SqliteRuntimeReader reader,
    ILogger<BeaconOutboxStore> logger,
    int maximumItems = BeaconOutboxStore.DefaultMaximumItems) : IBeaconTransmitQueue
{
    /// <summary>Most beacons that may be queued or in flight at once.</summary>
    public const int DefaultMaximumItems = 100;

    /// <summary>Retries after which a beacon reaches its terminal failure state.</summary>
    public const int MaximumAttempts = 3;

    /// <summary>How long a queued beacon stays worth transmitting.</summary>
    public static readonly TimeSpan DefaultExpiry = TimeSpan.FromMinutes(15);

    /// <summary>How long a terminal row is kept for diagnostics before pruning.</summary>
    internal static readonly TimeSpan TerminalRetention = TimeSpan.FromHours(24);

    public async Task<BeaconEnqueueResult> EnqueueAsync(
        Guid portId,
        LocationData location,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(location);

        (BeaconEnqueueResult Result, BeaconQueueItem? Evicted) outcome = await writer.EnqueueAsync(
            "beacon.enqueue", async (context, token) =>
        {
            await ExpireStaleAsync(context, now, token).ConfigureAwait(false);

            int activeCount = await context.BeaconOutbox
                .CountAsync(beacon => beacon.Status == BeaconQueueStatus.Queued
                    || beacon.Status == BeaconQueueStatus.Sending, token).ConfigureAwait(false);

            BeaconQueueItem? dropped = null;
            if (activeCount >= maximumItems)
            {
                BeaconOutboxRecord? oldestQueued = await context.BeaconOutbox
                    .Where(beacon => beacon.Status == BeaconQueueStatus.Queued)
                    .OrderBy(beacon => beacon.CreatedAt)
                    .FirstOrDefaultAsync(token).ConfigureAwait(false);

                if (oldestQueued is null)
                {
                    // Every slot is held by an in-flight send; reject the new beacon
                    // instead of evicting one that may already be on the air.
                    return (new BeaconEnqueueResult(null, null, BeaconQueueStatus.Dropped), null);
                }

                oldestQueued.Status = BeaconQueueStatus.Dropped;
                oldestQueued.Error = "Queue capacity reached.";
                oldestQueued.UpdatedAt = now.UtcDateTime;
                dropped = ToItem(oldestQueued);
            }

            BeaconOutboxRecord record = new()
            {
                Id = Guid.NewGuid(),
                PortId = portId == Guid.Empty ? null : portId,
                Latitude = location.Location.Latitude,
                Longitude = location.Location.Longitude,
                Altitude = location.Altitude,
                CreatedAt = now.UtcDateTime,
                ExpiresAt = now.Add(DefaultExpiry).UtcDateTime,
                AttemptCount = 0,
                Status = BeaconQueueStatus.Queued,
                UpdatedAt = now.UtcDateTime
            };

            context.BeaconOutbox.Add(record);
            await context.SaveChangesAsync(token).ConfigureAwait(false);

            return (new BeaconEnqueueResult(ToItem(record), dropped, BeaconQueueStatus.Queued), dropped);
        }, cancellationToken).ConfigureAwait(false);

        if (outcome.Evicted is { } evicted)
        {
            logger.LogWarning(
                "Beacon outbox full; dropped queued beacon {BeaconId} to admit a newer beacon.",
                evicted.Id);
        }

        return outcome.Result;
    }

    public Task<IReadOnlyList<BeaconQueueItem>> ReconcileAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken = default) =>
        writer.EnqueueAsync<IReadOnlyList<BeaconQueueItem>>("beacon.reconcile", async (context, token) =>
        {
            // An interrupted send must not stay "in flight" forever: the process that
            // owned it is gone, so the beacon returns to the queue and may be retried.
            await context.BeaconOutbox
                .Where(beacon => beacon.Status == BeaconQueueStatus.Sending)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(beacon => beacon.Status, BeaconQueueStatus.Queued)
                        .SetProperty(beacon => beacon.UpdatedAt, now.UtcDateTime),
                    token).ConfigureAwait(false);

            await ExpireStaleAsync(context, now, token).ConfigureAwait(false);
            await PruneTerminalAsync(context, now, token).ConfigureAwait(false);

            List<BeaconOutboxRecord> queued = await context.BeaconOutbox
                .Where(beacon => beacon.Status == BeaconQueueStatus.Queued)
                .OrderBy(beacon => beacon.CreatedAt)
                .ToListAsync(token).ConfigureAwait(false);

            return (IReadOnlyList<BeaconQueueItem>)[.. queued.Select(ToItem)];
        }, cancellationToken);

    public Task<IReadOnlyList<BeaconQueueItem>> GetSnapshotAsync(CancellationToken cancellationToken = default) =>
        reader.QueryAsync<IReadOnlyList<BeaconQueueItem>>(async (context, token) =>
        {
            List<BeaconOutboxRecord> records = await context.BeaconOutbox.AsNoTracking()
                .OrderBy(beacon => beacon.CreatedAt)
                .ToListAsync(token).ConfigureAwait(false);
            return [.. records.Select(ToItem)];
        }, cancellationToken);

    public Task MarkSendingAsync(Guid id, CancellationToken cancellationToken = default) =>
        TransitionAsync("beacon.mark-sending", id, (beacon, now) =>
        {
            if (beacon.Status != BeaconQueueStatus.Queued)
            {
                return false;
            }

            beacon.Status = BeaconQueueStatus.Sending;
            beacon.UpdatedAt = now.UtcDateTime;
            return true;
        }, cancellationToken);

    public Task MarkSentAsync(Guid id, CancellationToken cancellationToken = default) =>
        TransitionAsync("beacon.mark-sent", id, (beacon, now) =>
        {
            if (beacon.Status is BeaconQueueStatus.Sent or BeaconQueueStatus.Failed or BeaconQueueStatus.Expired)
            {
                return false;
            }

            beacon.Status = BeaconQueueStatus.Sent;
            beacon.UpdatedAt = now.UtcDateTime;
            return true;
        }, cancellationToken);

    public Task MarkAttemptFailedAsync(
        Guid id,
        string failureReason,
        DateTimeOffset now,
        CancellationToken cancellationToken = default) =>
        TransitionAsync("beacon.mark-failed", id, (beacon, transitionedAt) =>
        {
            if (beacon.Status is BeaconQueueStatus.Sent or BeaconQueueStatus.Failed or BeaconQueueStatus.Expired
                or BeaconQueueStatus.Dropped)
            {
                return false;
            }

            int attempts = beacon.AttemptCount + 1;
            beacon.AttemptCount = attempts;
            beacon.Error = failureReason;
            beacon.Status = attempts >= MaximumAttempts || transitionedAt >= ToUtcOffset(beacon.ExpiresAt)
                ? BeaconQueueStatus.Failed
                : BeaconQueueStatus.Queued;
            beacon.UpdatedAt = transitionedAt.UtcDateTime;
            return true;
        }, cancellationToken, now);

    /// <summary>
    /// Applies one state change. The read, the transition and the commit share one
    /// transaction, so two callers cannot interleave a check and an update.
    /// </summary>
    private Task TransitionAsync(
        string operation,
        Guid id,
        Func<BeaconOutboxRecord, DateTimeOffset, bool> apply,
        CancellationToken cancellationToken,
        DateTimeOffset? now = null) =>
        writer.EnqueueAsync(operation, async (context, token) =>
        {
            BeaconOutboxRecord? beacon = await context.BeaconOutbox
                .FirstOrDefaultAsync(candidate => candidate.Id == id, token).ConfigureAwait(false);
            if (beacon is null)
            {
                return false;
            }

            DateTimeOffset transitionedAt = now ?? DateTimeOffset.UtcNow;
            if (!apply(beacon, transitionedAt))
            {
                return false;
            }

            await context.SaveChangesAsync(token).ConfigureAwait(false);
            return true;
        }, cancellationToken);

    private static Task ExpireStaleAsync(
        AppDbContext context,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        context.BeaconOutbox
            .Where(beacon => (beacon.Status == BeaconQueueStatus.Queued
                    || beacon.Status == BeaconQueueStatus.Sending)
                && beacon.ExpiresAt <= now.UtcDateTime)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(beacon => beacon.Status, BeaconQueueStatus.Expired)
                    .SetProperty(beacon => beacon.Error, "Beacon expired before transmission.")
                    .SetProperty(beacon => beacon.UpdatedAt, now.UtcDateTime),
                cancellationToken);

    private static async Task PruneTerminalAsync(
        AppDbContext context,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        DateTime cutoff = now.Subtract(TerminalRetention).UtcDateTime;
        await context.BeaconOutbox
            .Where(beacon => (beacon.Status == BeaconQueueStatus.Sent
                    || beacon.Status == BeaconQueueStatus.Failed
                    || beacon.Status == BeaconQueueStatus.Expired
                    || beacon.Status == BeaconQueueStatus.Dropped)
                && beacon.UpdatedAt <= cutoff)
            .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
    }

    private static BeaconQueueItem ToItem(BeaconOutboxRecord record) => new(
        record.Id,
        record.PortId ?? Guid.Empty,
        record.Latitude,
        record.Longitude,
        record.Altitude,
        ToUtcOffset(record.CreatedAt),
        ToUtcOffset(record.ExpiresAt),
        record.AttemptCount,
        record.Status,
        record.Error);

    private static DateTimeOffset ToUtcOffset(DateTime value) => new(value, TimeSpan.Zero);
}
