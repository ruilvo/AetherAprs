// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Models.Aprs.Packets;
using AetherAprs.Services.Contracts;
using AetherAprs.Services.Diagnostics;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace AetherAprs.Services.Packets;

/// <summary>
/// Bounded, single-consumer inbound ingest queue. See
/// <see cref="IPacketIngestPipeline"/> for the ordering guarantee.
/// </summary>
internal sealed class PacketIngestPipeline : IPacketIngestPipeline
{
    private readonly IPacketStorageService _packetStorageService;
    private readonly ILogger<PacketIngestPipeline> _logger;
    private readonly IDiagnosticEventSink? _diagnosticSink;
    private readonly Channel<QueuedPacket> _channel;
    private readonly Lock _startLock = new();
    private Task? _consumerTask;

    // Intentionally not disposed: queued callbacks may still reference the token,
    // and disposing it can throw ObjectDisposedException into modem threads.
    private readonly CancellationTokenSource _disposalCts = new();

    // Inbound drops are counted per ingress port so a saturated or failing ingest
    // path is visible instead of silently losing packets (audit C-05/C-07).
    private readonly ConcurrentDictionary<Guid, long> _droppedPacketCounts = new();
    private long _enqueuedPackets;
    private long _storedPackets;
    private long _totalDroppedPackets;
    private bool _disposed;

    public PacketIngestPipeline(
        IPacketStorageService packetStorageService,
        ILogger<PacketIngestPipeline> logger,
        IDiagnosticEventSink? diagnosticSink = null,
        PacketIngestOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(packetStorageService);
        ArgumentNullException.ThrowIfNull(logger);

        Options = Validate(options ?? PacketIngestOptions.Default);
        _packetStorageService = packetStorageService;
        _logger = logger;
        _diagnosticSink = diagnosticSink;

        // Bound the queue so a burst cannot grow it unboundedly; the single
        // consumer also guarantees packets are persisted and published in receive
        // order. Producers never block, so a saturated queue drops the newest
        // packet and counts it (see Accept).
        _channel = Channel.CreateBounded<QueuedPacket>(new BoundedChannelOptions(Options.QueueCapacity)
        {
            SingleReader = true,
            FullMode = BoundedChannelFullMode.Wait
        });
    }

    public event EventHandler<PortPacketReceivedEventArgs>? PacketStored;

    public PacketIngestOptions Options { get; }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_startLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _consumerTask ??= Task.Run(ConsumeAsync, CancellationToken.None);
        }

        return Task.CompletedTask;
    }

    public long TotalDroppedPackets => Interlocked.Read(ref _totalDroppedPackets);

    public void Accept(Guid portId, AprsPacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);

        // TryWrite is intentional: this runs on the modem read loop, which must not
        // block on the database. A full queue therefore drops the newest packet and
        // records it below rather than waiting (audit C-07).
        if (_channel.Writer.TryWrite(new QueuedPacket(portId, packet)))
        {
            Interlocked.Increment(ref _enqueuedPackets);
            return;
        }

        RecordDroppedPacket(portId, packet, "Inbound queue full; newest packet dropped.");
    }

    public long GetDroppedPacketCount(Guid portId) =>
        _droppedPacketCounts.TryGetValue(portId, out var count) ? count : 0;

    public PacketIngestMetrics GetMetrics() => new(
        Interlocked.Read(ref _enqueuedPackets),
        Interlocked.Read(ref _storedPackets),
        Interlocked.Read(ref _totalDroppedPackets));

    private async Task ConsumeAsync()
    {
        try
        {
            await foreach (var queued in _channel.Reader.ReadAllAsync(_disposalCts.Token).ConfigureAwait(false))
            {
                await ProcessPacketAsync(queued.PortId, queued.Packet).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during disposal.
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Packet processing queue terminated unexpectedly.");
        }
    }

    private async Task ProcessPacketAsync(Guid portId, AprsPacket packet)
    {
        if (!await TryStorePacketAsync(portId, packet).ConfigureAwait(false))
        {
            // Consumers read the database to render, so publishing an unstored
            // packet would make the UI and the store diverge permanently
            // (audit C-05). Drop it loudly instead.
            RecordDroppedPacket(portId, packet, "Packet could not be persisted.");
            return;
        }

        Interlocked.Increment(ref _storedPackets);

        // Raise the event after storage so consumers see persisted packets in order.
        try
        {
            PacketStored?.Invoke(this, new PortPacketReceivedEventArgs
            {
                PortId = portId,
                Packet = packet
            });
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Exception in a stored-packet subscriber for packet from {Source}",
                packet.Source);
        }
    }

    /// <summary>
    /// Persists an inbound packet, retrying transient SQLite contention a bounded
    /// number of times. Returns <see langword="false"/> when the packet must not be
    /// published because it could not be stored.
    /// </summary>
    private async Task<bool> TryStorePacketAsync(Guid portId, AprsPacket packet)
    {
        for (var attempt = 1; attempt <= Options.StoreAttemptLimit; attempt++)
        {
            try
            {
                await _packetStorageService
                    .StorePacketAsync(packet, portId, isOutbound: false, _disposalCts.Token)
                    .ConfigureAwait(false);
                return true;
            }
            catch (OperationCanceledException) when (_disposalCts.IsCancellationRequested)
            {
                // Expected during disposal; nothing to publish.
                return false;
            }
            catch (SqliteException exception) when (
                attempt < Options.StoreAttemptLimit && IsTransientSqliteFailure(exception))
            {
                _logger.LogWarning(
                    exception,
                    "Transient SQLite failure storing packet from {Source} on port {PortId} (attempt {Attempt}/{Limit}); retrying.",
                    packet.Source,
                    portId,
                    attempt,
                    Options.StoreAttemptLimit);

                if (!await DelayBeforeRetryAsync(attempt).ConfigureAwait(false))
                {
                    return false;
                }
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    exception,
                    "Failed to store packet from {Source} on port {PortId}; the packet was not published.",
                    packet.Source,
                    portId);
                return false;
            }
        }

        return false;
    }

    private static bool IsTransientSqliteFailure(SqliteException exception) =>
        // SQLITE_BUSY (5) and SQLITE_LOCKED (6) are the two contention codes that
        // clear on their own; anything else is a programming or storage fault.
        exception.SqliteErrorCode is 5 or 6;

    private async Task<bool> DelayBeforeRetryAsync(int attempt)
    {
        try
        {
            var delay = TimeSpan.FromTicks(Options.StoreRetryDelay.Ticks * attempt);
            await Task.Delay(delay, _disposalCts.Token).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private void RecordDroppedPacket(Guid portId, AprsPacket packet, string reason)
    {
        if (_disposed)
        {
            // Shutdown is not a drop: the caller is tearing the runtime down.
            return;
        }

        _droppedPacketCounts.AddOrUpdate(portId, 1, static (_, current) => current + 1);
        Interlocked.Increment(ref _totalDroppedPackets);

        _diagnosticSink?.Record(
            "ingest",
            DiagnosticSeverity.Error,
            "Dropped",
            reason,
            portId.ToString("N"));

        _logger.LogWarning(
            "Dropped inbound packet from {Source} on port {PortId}: {Reason}",
            packet.Source,
            portId,
            reason);
    }

    private static PacketIngestOptions Validate(PacketIngestOptions options)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(options.QueueCapacity, 1, nameof(options.QueueCapacity));
        ArgumentOutOfRangeException.ThrowIfLessThan(options.StoreAttemptLimit, 1, nameof(options.StoreAttemptLimit));
        ArgumentOutOfRangeException.ThrowIfLessThan(options.StoreRetryDelay, TimeSpan.Zero, nameof(options.StoreRetryDelay));
        return options;
    }

    public async ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);

        Task? consumer;

        lock (_startLock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            consumer = _consumerTask;
        }

        _disposalCts.Cancel();
        _channel.Writer.TryComplete();

        if (consumer is null)
        {
            return;
        }

        try
        {
            await consumer.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected during disposal.
        }
    }

    private sealed record QueuedPacket(Guid PortId, AprsPacket Packet);
}
