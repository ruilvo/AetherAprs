// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using AetherAprs.Models;

namespace AetherAprs.Services.Transmission;

/// <summary>Lifecycle of one beacon waiting to reach a modem.</summary>
public enum BeaconQueueStatus
{
    Queued,
    Sending,
    Sent,
    Expired,
    Failed,
    Dropped
}

/// <summary>One durable beacon outbox entry.</summary>
public sealed record BeaconQueueItem(
    Guid Id,
    Guid PortId,
    double Latitude,
    double Longitude,
    double? Altitude,
    DateTimeOffset CreatedUtc,
    DateTimeOffset ExpiresUtc,
    int AttemptCount = 0,
    BeaconQueueStatus Status = BeaconQueueStatus.Queued,
    string? Error = null);

/// <summary>
/// Outcome of an <see cref="IBeaconTransmitQueue.EnqueueAsync"/> call.
/// </summary>
/// <param name="Queued">The beacon that joined the queue, or <see langword="null"/> when it was rejected.</param>
/// <param name="Dropped">The previously queued beacon evicted to make room, if any.</param>
/// <param name="Status">The resulting status of the enqueue attempt.</param>
public readonly record struct BeaconEnqueueResult(
    BeaconQueueItem? Queued,
    BeaconQueueItem? Dropped,
    BeaconQueueStatus Status);

/// <summary>
/// The durable beacon outbox. Every transition is committed before the call
/// returns, so a beacon accepted here survives a process restart.
/// </summary>
public interface IBeaconTransmitQueue
{
    /// <summary>
    /// Adds a beacon to the outbox. When the outbox is full, the oldest queued
    /// beacon is evicted and reported in the result instead of throwing.
    /// </summary>
    /// <param name="portId">
    /// The target port, or <see cref="Guid.Empty"/> when the beacon may use any
    /// usable transmit route.
    /// </param>
    /// <param name="location">The captured position to transmit.</param>
    /// <param name="now">The current instant, supplied by the caller's clock.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    Task<BeaconEnqueueResult> EnqueueAsync(
        Guid portId,
        LocationData location,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns interrupted sends to the queue, expires stale beacons and returns
    /// the beacons that are ready to transmit.
    /// </summary>
    Task<IReadOnlyList<BeaconQueueItem>> ReconcileAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    /// <summary>Reads every retained outbox entry, newest last.</summary>
    Task<IReadOnlyList<BeaconQueueItem>> GetSnapshotAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Counts the retained beacons that were never transmitted because they expired
    /// before their turn or were dropped to admit a newer beacon. Lets the UI surface
    /// that beacon work was lost instead of failing silently.
    /// </summary>
    Task<int> GetStaleCountAsync(CancellationToken cancellationToken = default);

    /// <summary>Marks a queued beacon as being transmitted.</summary>
    Task MarkSendingAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Marks a beacon as transmitted.</summary>
    Task MarkSentAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records a failed attempt. The beacon returns to the queue, or reaches its
    /// terminal failure state once the bounded retry budget or the expiry is spent.
    /// </summary>
    Task MarkAttemptFailedAsync(
        Guid id,
        string failureReason,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);
}
