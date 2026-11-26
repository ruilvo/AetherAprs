// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace AetherAprs.Services.Ports;

/// <summary>
/// Serializes session-state persistence and coalesces bursts of transitions into a
/// single write of the newest snapshot.
/// </summary>
/// <remarks>
/// The queue holds one request: a request that arrives while a write is in flight
/// replaces any request that is still waiting, so a reconnect storm produces at most
/// one write per completed write. Callers that need the state on disk before they
/// continue use <see cref="FlushAsync"/> with their own deadline.
/// </remarks>
internal sealed class SessionStateWriter : IAsyncDisposable
{
    private const int QueueCapacity = 1;

    private readonly ISessionStateStore _stateStore;
    private readonly ILogger _logger;
    private readonly Channel<WriteRequest> _requests;
    private readonly Lock _progressLock = new();
    private readonly Lock _startLock = new();

    private TaskCompletionSource<bool> _progress = CreateProgressSource();
    private Task? _writerTask;
    private long _requestedVersion;
    private long _durableVersion;
    private bool _disposed;

    public SessionStateWriter(ISessionStateStore stateStore, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(stateStore);
        ArgumentNullException.ThrowIfNull(logger);

        _stateStore = stateStore;
        _logger = logger;
        _requests = Channel.CreateBounded<WriteRequest>(new BoundedChannelOptions(QueueCapacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });
    }

    /// <summary>
    /// Starts the write loop. Construction only wires the queue, so the coordinator
    /// decides when runtime transitions begin to be persisted; calling this again
    /// has no effect.
    /// </summary>
    public void Start()
    {
        lock (_startLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _writerTask ??= WriteLoopAsync();
        }
    }

    /// <summary>
    /// Returns the version of the newest snapshot that reached the store.
    /// </summary>
    public long DurableVersion => Volatile.Read(ref _durableVersion);

    /// <summary>
    /// Queues a snapshot for persistence. Older pending snapshots are superseded.
    /// </summary>
    public void Request(SessionSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (Volatile.Read(ref _disposed))
        {
            return;
        }

        long version = Interlocked.Increment(ref _requestedVersion);
        _requests.Writer.TryWrite(new WriteRequest(snapshot, version));
    }

    /// <summary>
    /// Completes once every snapshot requested before this call has been written.
    /// </summary>
    /// <exception cref="OperationCanceledException">
    /// The caller's deadline expired before the store accepted the snapshots.
    /// </exception>
    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        long target = Volatile.Read(ref _requestedVersion);
        if (target == 0)
        {
            return;
        }

        while (true)
        {
            Task progress;
            lock (_progressLock)
            {
                if (_durableVersion >= target)
                {
                    return;
                }

                progress = _progress.Task;
            }

            await progress.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task WriteLoopAsync()
    {
        while (await _requests.Reader.WaitToReadAsync().ConfigureAwait(false))
        {
            WriteRequest latest = default;
            bool pending = false;
            while (_requests.Reader.TryRead(out WriteRequest request))
            {
                latest = request;
                pending = true;
            }

            if (!pending)
            {
                continue;
            }

            try
            {
                await _stateStore
                    .SaveAsync(latest.Snapshot, CancellationToken.None)
                    .ConfigureAwait(false);
                MarkDurable(latest.Version);
            }
            catch (Exception exception)
            {
                // Diagnostics are advisory: a failed write must not stop the
                // supervisor. The next transition queues a fresh snapshot.
                _logger.LogError(exception, "Session state could not be persisted.");
            }
        }
    }

    private void MarkDurable(long version)
    {
        lock (_progressLock)
        {
            if (version <= _durableVersion)
            {
                return;
            }

            _durableVersion = version;
            TaskCompletionSource<bool> completed = _progress;
            _progress = CreateProgressSource();
            completed.TrySetResult(true);
        }
    }

    private static TaskCompletionSource<bool> CreateProgressSource() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _requests.Writer.TryComplete();
        await (_writerTask ?? Task.CompletedTask).ConfigureAwait(false);
    }

    private readonly record struct WriteRequest(SessionSnapshot Snapshot, long Version);
}
