// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Models.Aprs.Packets;
using AetherAprs.Services.Contracts;
using System;
using System.Threading.Tasks;

namespace AetherAprs.Services.Packets;

/// <summary>
/// Bounded ingest policy: how many packets may wait, how hard a packet is retried
/// against transient storage contention, and how long to wait between attempts.
/// </summary>
public sealed record PacketIngestOptions
{
    /// <summary>Queue capacity used when the caller does not override it.</summary>
    public static PacketIngestOptions Default { get; } = new();

    /// <summary>Number of queued inbound packets before the newest is dropped.</summary>
    public int QueueCapacity { get; init; } = 1024;

    /// <summary>Total storage attempts for one inbound packet, including the first.</summary>
    public int StoreAttemptLimit { get; init; } = 3;

    /// <summary>Base delay between storage attempts; the attempt number scales it.</summary>
    public TimeSpan StoreRetryDelay { get; init; } = TimeSpan.FromMilliseconds(50);
}

/// <summary>
/// Observable counters for the ingest path. <paramref name="Enqueued"/> counts
/// packets accepted for processing, <paramref name="Stored"/> counts those that
/// reached durable storage and were published, and <paramref name="Dropped"/>
/// counts packets that could neither be queued nor stored.
/// </summary>
public sealed record PacketIngestMetrics(long Enqueued, long Stored, long Dropped);

/// <summary>
/// Owns the inbound packet queue and its single consumer. Producers hand packets
/// over without blocking; the consumer persists each packet in receive order and
/// then publishes it. A packet is published only after it was stored, so consumers
/// that read the database can never see a divergence between what was live and
/// what was durable.
/// </summary>
public interface IPacketIngestPipeline : IAsyncDisposable
{
    /// <summary>
    /// Raised on the ingest consumer after the packet was stored, in receive order.
    /// </summary>
    event EventHandler<PortPacketReceivedEventArgs>? PacketStored;

    /// <summary>The policy this pipeline was built with.</summary>
    PacketIngestOptions Options { get; }

    /// <summary>
    /// Queues a received packet. Never blocks and never throws; a full queue is
    /// reported through <see cref="GetDroppedPacketCount"/>.
    /// </summary>
    void Accept(Guid portId, AprsPacket packet);

    /// <summary>Inbound packets dropped for a port, whether queued or stored.</summary>
    long GetDroppedPacketCount(Guid portId);

    /// <summary>Inbound packets dropped across all ports.</summary>
    long TotalDroppedPackets { get; }

    /// <summary>Current counters for the ingest path.</summary>
    PacketIngestMetrics GetMetrics();
}
