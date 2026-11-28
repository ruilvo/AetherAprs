// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using AetherAprs.Models.Aprs.Packets;

namespace AetherAprs.Services.Ports;

/// <summary>
/// Bounded forwarding policy: how many candidates may wait, how old a candidate
/// may be before it is discarded instead of transmitted, and how long one forward
/// may occupy the relay consumer.
/// </summary>
public sealed record DigipeatRouterOptions
{
    /// <summary>Policy used when the caller does not override it.</summary>
    public static DigipeatRouterOptions Default { get; } = new();

    /// <summary>Number of queued candidates before an incoming one is discarded.</summary>
    public int QueueCapacity { get; init; } = 256;

    /// <summary>
    /// A candidate older than this is discarded rather than transmitted: a relay
    /// that waited behind a blocked route is stale, and a late RF frame is worse
    /// than a missed one.
    /// </summary>
    public TimeSpan MaxCandidateAge { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Maximum time one digipeat transmission may occupy the consumer.</summary>
    public TimeSpan TransmitTimeout { get; init; } = TimeSpan.FromSeconds(30);
}

/// <summary>
/// Observable counters for the forwarding path. <paramref name="Discarded"/>
/// counts candidates that were never transmitted because the queue was full, the
/// candidate had aged out, or a route was no longer available;
/// <paramref name="Failed"/> counts transmissions that failed.
/// </summary>
public sealed record DigipeatMetrics(long Enqueued, long Forwarded, long Discarded, long Failed);

/// <summary>
/// Owns the digipeat queue and its single consumer. Forwarding decisions are made
/// when a candidate is dequeued, not when it arrives, so a route is always
/// evaluated against current configuration and current running sessions. A port
/// that is not running never receives a forward, and a forward never starts a
/// port: only operator enablement may do that.
/// </summary>
public interface IDigipeatRouter : IAsyncDisposable
{
    /// <summary>
    /// Starts the consumer that drains the candidate queue. Construction only wires
    /// the queue, so the container can build the router before the session exists;
    /// calling this again has no effect.
    /// </summary>
    /// <param name="cancellationToken">Cancels the start itself, not the consumer.</param>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>Raised after a packet was handed to a running target session.</summary>
    event EventHandler<PortPacketTransmittedEventArgs>? PacketForwarded;

    /// <summary>The policy this router was built with.</summary>
    DigipeatRouterOptions Options { get; }

    /// <summary>
    /// Queues a stored packet for forwarding. Never blocks and never throws; a
    /// discarded candidate is reported through
    /// <see cref="GetDiscardedCandidateCount"/>.
    /// </summary>
    void Enqueue(Guid sourcePortId, AprsPacket packet);

    /// <summary>Forwarding candidates discarded for a source port.</summary>
    long GetDiscardedCandidateCount(Guid sourcePortId);

    /// <summary>Forwarding candidates discarded across all source ports.</summary>
    long TotalDiscardedCandidates { get; }

    /// <summary>Current counters for the forwarding path.</summary>
    DigipeatMetrics GetMetrics();

    /// <summary>
    /// Gets the bounded route/reason history shared with the forwarding policy,
    /// oldest first. Records carry ports, time and reason only — never payload.
    /// </summary>
    /// <param name="maximum">Maximum number of records to return.</param>
    IReadOnlyList<DigipeatDecisionRecord> GetRecentDecisions(int maximum = DigipeatDecisionLog.DefaultCapacity);

    /// <summary>Gets how many forwarding decisions were recorded with the given reason.</summary>
    /// <param name="reason">The accept or reject reason.</param>
    long GetDecisionCount(DigipeatReason reason);
}
