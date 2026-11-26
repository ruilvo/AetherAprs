// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace AetherAprs.Services.Transmission;

public enum BeaconQueueStatus
{
    Queued,
    Sending,
    Sent,
    Expired,
    Failed,
    Dropped
}

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
/// Outcome of an <see cref="IBeaconTransmitQueue.Enqueue"/> call.
/// </summary>
/// <param name="Queued">The beacon that joined the queue, or <see langword="null"/> when it was rejected.</param>
/// <param name="Dropped">The previously queued beacon evicted to make room, if any.</param>
/// <param name="Status">The resulting status of the enqueue attempt.</param>
public readonly record struct BeaconEnqueueResult(
    BeaconQueueItem? Queued,
    BeaconQueueItem? Dropped,
    BeaconQueueStatus Status);

public interface IBeaconTransmitQueue
{
    /// <summary>
    /// Adds a beacon to the outbox. When the outbox is full, the oldest queued
    /// beacon is evicted and reported in the result instead of throwing.
    /// </summary>
    BeaconEnqueueResult Enqueue(Guid portId, LocationData location, DateTimeOffset now);

    IReadOnlyList<BeaconQueueItem> Reconcile(DateTimeOffset now);

    void MarkSending(Guid id);

    void MarkSent(Guid id);

    void MarkAttemptFailed(Guid id, string failureReason, DateTimeOffset now);

    IReadOnlyList<BeaconQueueItem> Snapshot { get; }
}

/// <summary>
/// Bounded, atomic file-backed beacon outbox. Interrupted sends are returned to
/// the queued state when the process constructs the store again.
/// </summary>
/// <remarks>
/// State transitions mutate an in-memory list and set a dirty flag; a single
/// background writer persists the latest snapshot. The writer is signalled through
/// a bounded <see cref="Channel{T}"/> of capacity one with
/// <see cref="BoundedChannelFullMode.DropWrite"/>, so rapid transitions coalesce
/// into one write instead of rewriting the whole file per transition. Disposal
/// stops the writer (bounded) before returning, so no background thread touches
/// the state after the queue is gone.
/// </remarks>
public sealed class FileBeaconTransmitQueue : IBeaconTransmitQueue, IDisposable
{
    public const int DefaultMaximumItems = 100;
    public const int MaximumAttempts = 3;
    public static readonly TimeSpan DefaultExpiry = TimeSpan.FromMinutes(15);

    /// <summary>Retries after a transient write failure before waiting for the next mutation.</summary>
    private const int PersistRetryLimit = 3;

    private static readonly TimeSpan PersistRetryDelay = TimeSpan.FromMilliseconds(250);

    private readonly string _path;
    private readonly int _maximumItems;
    private readonly ILogger<FileBeaconTransmitQueue> _logger;
    private readonly Lock _gate = new();
    private readonly List<BeaconQueueItem> _items;

    // Coalescing signal: one pending byte is enough, extra signals are dropped
    // because the writer always serializes the latest in-memory snapshot.
    private readonly Channel<bool> _persistSignal = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropWrite
        });

    private readonly CancellationTokenSource _disposalCts = new();
    private readonly Task _persistWriterTask;
    private readonly Lock _idleLock = new();
    private TaskCompletionSource _idleSignal = CreateIdleSignal();
    private bool _persistPending;
    private bool _writeInFlight;
    private bool _disposed;
    private int _persistRetryAttempts;
    private int _persistWriteCount;

    public FileBeaconTransmitQueue(
        string path,
        ILogger<FileBeaconTransmitQueue> logger,
        int maximumItems = DefaultMaximumItems)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(logger);
        if (maximumItems <= 0)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumItems);
        }

        _path = path;
        _maximumItems = maximumItems;
        _logger = logger;
        _items = Load(path);
        ReconcileLocked(DateTimeOffset.UtcNow);
        _persistWriterTask = Task.Run(PersistLoopAsync);
    }

    /// <summary>
    /// Test hook awaited before each file write so a test can hold a persist open.
    /// </summary>
    internal Func<CancellationToken, Task>? PersistBarrierForTests { get; set; }

    /// <summary>
    /// Number of snapshot writes that completed successfully; used by tests to
    /// prove that rapid transitions coalesce.
    /// </summary>
    internal int PersistWriteCount => Volatile.Read(ref _persistWriteCount);

    /// <summary>
    /// Completes once every mutation requested before this call has reached the outbox
    /// file. Used by tests instead of polling the file, and by callers that need the
    /// durable snapshot before they continue.
    /// </summary>
    internal async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        while (true)
        {
            Task idle;
            lock (_idleLock)
            {
                idle = _idleSignal.Task;
            }

            lock (_gate)
            {
                if (!_persistPending && !_writeInFlight && _persistSignal.Reader.Count == 0)
                {
                    return;
                }
            }

            await idle.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static TaskCompletionSource CreateIdleSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private void SignalIdle()
    {
        TaskCompletionSource completed;
        lock (_idleLock)
        {
            completed = _idleSignal;
            _idleSignal = CreateIdleSignal();
        }

        completed.TrySetResult();
    }

    /// <summary>
    /// Maximum time <see cref="Dispose"/> waits for an in-flight write. Tests
    /// shorten it; production keeps the bounded default so shutdown cannot hang.
    /// </summary>
    internal TimeSpan PersistShutdownTimeout { get; set; } = TimeSpan.FromSeconds(5);

    public IReadOnlyList<BeaconQueueItem> Snapshot
    {
        get
        {
            lock (_gate)
            {
                return [.. _items];
            }
        }
    }

    public BeaconEnqueueResult Enqueue(Guid portId, LocationData location, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(location);
        lock (_gate)
        {
            ReconcileLocked(now);
            int activeCount = _items.Count(item =>
                item.Status is BeaconQueueStatus.Queued or BeaconQueueStatus.Sending);
            BeaconQueueItem? dropped = null;
            if (activeCount >= _maximumItems)
            {
                int droppedIndex = _items.FindIndex(item => item.Status == BeaconQueueStatus.Queued);
                if (droppedIndex < 0)
                {
                    // Every slot is held by an in-flight send; reject the new beacon
                    // instead of evicting one that may already be on the air.
                    return new BeaconEnqueueResult(null, null, BeaconQueueStatus.Dropped);
                }

                dropped = _items[droppedIndex] with
                {
                    Status = BeaconQueueStatus.Dropped,
                    Error = "Queue capacity reached."
                };
                _items[droppedIndex] = dropped;
            }

            // The newest location wins: evict the oldest queued beacon to admit it.
            BeaconQueueItem item = new(
                Guid.NewGuid(),
                portId,
                location.Location.Latitude,
                location.Location.Longitude,
                location.Altitude,
                now,
                now.Add(DefaultExpiry));
            _items.Add(item);
            MarkDirtyLocked();
            return new BeaconEnqueueResult(item, dropped, BeaconQueueStatus.Queued);
        }
    }

    public IReadOnlyList<BeaconQueueItem> Reconcile(DateTimeOffset now)
    {
        lock (_gate)
        {
            ReconcileLocked(now);
            return [.. _items.Where(item => item.Status == BeaconQueueStatus.Queued)];
        }
    }

    public void MarkSending(Guid id) => Update(id, item => item with { Status = BeaconQueueStatus.Sending });

    public void MarkSent(Guid id) => Update(id, item => item with { Status = BeaconQueueStatus.Sent });

    public void MarkAttemptFailed(Guid id, string failureReason, DateTimeOffset now)
    {
        lock (_gate)
        {
            int index = _items.FindIndex(item => item.Id == id);
            if (index < 0)
            {
                return;
            }

            BeaconQueueItem item = _items[index];
            int attempts = item.AttemptCount + 1;
            _items[index] = item with
            {
                AttemptCount = attempts,
                Status = attempts >= MaximumAttempts || now >= item.ExpiresUtc
                    ? BeaconQueueStatus.Failed
                    : BeaconQueueStatus.Queued,
                Error = failureReason
            };
            MarkDirtyLocked();
        }
    }

    private void ReconcileLocked(DateTimeOffset now)
    {
        bool changed = false;
        for (int index = 0; index < _items.Count; index++)
        {
            BeaconQueueItem item = _items[index];
            if (item.Status == BeaconQueueStatus.Sending)
            {
                _items[index] = item with { Status = BeaconQueueStatus.Queued };
                changed = true;
                item = _items[index];
            }

            if (item.Status is BeaconQueueStatus.Queued or BeaconQueueStatus.Sending
                && now >= item.ExpiresUtc)
            {
                _items[index] = item with
                {
                    Status = BeaconQueueStatus.Expired,
                    Error = "Beacon expired before transmission."
                };
                changed = true;
            }
        }

        if (changed)
        {
            MarkDirtyLocked();
        }
    }

    private void Update(Guid id, Func<BeaconQueueItem, BeaconQueueItem> update)
    {
        lock (_gate)
        {
            int index = _items.FindIndex(item => item.Id == id);
            if (index >= 0)
            {
                _items[index] = update(_items[index]);
                MarkDirtyLocked();
            }
        }
    }

    /// <summary>
    /// Marks the in-memory snapshot dirty and wakes the writer. Must be called
    /// while holding <see cref="_gate"/>.
    /// </summary>
    private void MarkDirtyLocked()
    {
        _persistPending = true;
        // A new mutation restarts the bounded retry budget for transient write failures.
        _persistRetryAttempts = 0;
        // A pending signal already guarantees a write of the latest snapshot.
        _persistSignal.Writer.TryWrite(true);
    }

    private async Task PersistLoopAsync()
    {
        CancellationToken cancellationToken = _disposalCts.Token;
        try
        {
            while (await _persistSignal.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
            {
                // Drain every pending signal: one write covers all coalesced transitions.
                while (_persistSignal.Reader.TryRead(out _))
                {
                }

                await PersistPendingAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when disposal gives up on a stuck write.
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "The beacon outbox writer stopped unexpectedly.");
        }
    }

    private async Task PersistPendingAsync(CancellationToken cancellationToken)
    {
        string json;
        try
        {
            lock (_gate)
            {
                if (!_persistPending)
                {
                    return;
                }

                json = JsonSerializer.Serialize(_items, BeaconQueueJsonContext.Default.ListBeaconQueueItem);
                _persistPending = false;
                _writeInFlight = true;
            }
        }
        catch (Exception exception)
        {
            // Keep the dirty flag so the next mutation or disposal retries.
            _logger.LogError(exception, "Failed to serialize the beacon outbox at {Path}.", _path);
            return;
        }

        try
        {
            if (PersistBarrierForTests is { } barrier)
            {
                await barrier(cancellationToken).ConfigureAwait(false);
            }

            await WriteSnapshotAsync(json, cancellationToken).ConfigureAwait(false);
            Interlocked.Increment(ref _persistWriteCount);
            lock (_gate)
            {
                _persistRetryAttempts = 0;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            bool retry;
            lock (_gate)
            {
                _persistPending = true;
                _persistRetryAttempts++;
                retry = _persistRetryAttempts <= PersistRetryLimit;
            }

            _logger.LogError(exception, "Failed to persist the beacon outbox to {Path}.", _path);

            if (!retry)
            {
                return;
            }

            try
            {
                await Task.Delay(PersistRetryDelay, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            _persistSignal.Writer.TryWrite(true);
        }
        finally
        {
            lock (_gate)
            {
                _writeInFlight = false;
            }

            SignalIdle();
        }
    }

    private async Task WriteSnapshotAsync(string json, CancellationToken cancellationToken)
    {
        string? directory = Path.GetDirectoryName(_path);
        if (directory is not null)
        {
            Directory.CreateDirectory(directory);
        }

        string temporaryPath = $"{_path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllTextAsync(temporaryPath, json, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, _path, overwrite: true);
        }
        finally
        {
            TryDeleteFile(temporaryPath);
        }
    }

    private List<BeaconQueueItem> Load(string path)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize(
                File.ReadAllText(path),
                BeaconQueueJsonContext.Default.ListBeaconQueueItem) ?? [];
        }
        catch (Exception exception) when (exception is JsonException
            or IOException
            or UnauthorizedAccessException)
        {
            QuarantineCorruptFile(exception);
            return [];
        }
    }

    /// <summary>
    /// Moves an unreadable outbox aside so the corrupt data is preserved for
    /// diagnosis and a fresh outbox can be written (ADR-011).
    /// </summary>
    private void QuarantineCorruptFile(Exception exception)
    {
        _logger.LogWarning(
            exception,
            "Beacon outbox at {Path} could not be read; quarantining it and starting with an empty queue.",
            _path);

        try
        {
            string quarantinePath = BuildQuarantinePath();
            File.Move(_path, quarantinePath);
            _logger.LogWarning(
                "Corrupt beacon outbox moved to {QuarantinePath}.",
                quarantinePath);
        }
        catch (Exception quarantineException) when (quarantineException is IOException
            or UnauthorizedAccessException)
        {
            _logger.LogError(
                quarantineException,
                "Failed to quarantine the corrupt beacon outbox at {Path}.",
                _path);
        }
    }

    private string BuildQuarantinePath()
    {
        string timestamp = DateTimeOffset.UtcNow.ToString(
            "yyyyMMddHHmmssfff",
            CultureInfo.InvariantCulture);
        string candidate = $"{_path}.corrupt-{timestamp}";
        int suffix = 1;
        while (File.Exists(candidate))
        {
            candidate = $"{_path}.corrupt-{timestamp}-{suffix}";
            suffix++;
        }

        return candidate;
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Best-effort cleanup of a temporary file; the snapshot itself is safe.
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (_persistPending)
            {
                _persistSignal.Writer.TryWrite(true);
            }
        }

        // Completing the channel drains pending signals and then lets the writer
        // exit; a final dirty snapshot is still written before it stops.
        _persistSignal.Writer.TryComplete();

        try
        {
            if (!_persistWriterTask.Wait(PersistShutdownTimeout))
            {
                _logger.LogWarning(
                    "Beacon outbox writer did not stop within {Timeout}; cancelling the in-flight write.",
                    PersistShutdownTimeout);
                _disposalCts.Cancel();
                if (!_persistWriterTask.Wait(PersistShutdownTimeout))
                {
                    _logger.LogError(
                        "Beacon outbox writer did not stop after cancellation; its last write may be incomplete.");
                }
            }
        }
        catch (AggregateException exception)
        {
            _logger.LogError(exception, "Beacon outbox writer failed during disposal.");
        }

        // The writer reads the token; only dispose the source once it has stopped.
        if (_persistWriterTask.IsCompleted)
        {
            _disposalCts.Dispose();
        }

        GC.SuppressFinalize(this);
    }
}
