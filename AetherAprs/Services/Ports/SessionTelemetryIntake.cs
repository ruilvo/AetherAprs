// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace AetherAprs.Services.Ports;

internal enum SessionTelemetryKind
{
    Receive,
    Transmit,
    Fault
}

/// <summary>
/// One transport observation reported by a running port.
/// </summary>
internal readonly record struct SessionTelemetryObservation(
    Guid PortId,
    SessionTelemetryKind Kind,
    Exception? Error);

/// <summary>
/// Bounds and serializes the transport observations that reach the session
/// coordinator.
/// </summary>
/// <remarks>
/// A busy feed produces thousands of observations per minute while the coordinator
/// only needs the newest timestamp per port. The queue therefore has a fixed
/// capacity, drops the observation that does not fit instead of blocking the modem
/// read loop, and counts every drop so the loss is observable. A fault observation
/// that does not fit is recoverable: the port keeps its last status and the next
/// fault or reconnect attempt reports again.
/// </remarks>
internal sealed class SessionTelemetryIntake : IAsyncDisposable
{
    public const int DefaultQueueCapacity = 1024;

    private readonly Channel<SessionTelemetryObservation> _observations;
    private readonly Func<SessionTelemetryObservation, Task> _handler;
    private readonly ILogger _logger;
    private readonly Task _consumerTask;
    private readonly Lock _idleLock = new();

    private TaskCompletionSource _idleSignal = CreateIdleSignal();
    private long _posted;
    private long _processed;
    private long _dropped;
    private bool _disposed;

    public SessionTelemetryIntake(
        Func<SessionTelemetryObservation, Task> handler,
        ILogger logger,
        int queueCapacity = DefaultQueueCapacity)
    {
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(queueCapacity);

        _handler = handler;
        _logger = logger;
        QueueCapacity = queueCapacity;
        _observations = Channel.CreateBounded<SessionTelemetryObservation>(
            new BoundedChannelOptions(queueCapacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false
            });
        _consumerTask = ConsumeAsync();
    }

    public int QueueCapacity { get; }

    /// <summary>
    /// Observations discarded because the queue was full.
    /// </summary>
    public long DroppedObservations => Volatile.Read(ref _dropped);

    /// <summary>
    /// Queues an observation without blocking the caller. Returns
    /// <see langword="false"/> when the observation was dropped.
    /// </summary>
    public bool Post(SessionTelemetryObservation observation)
    {
        if (Volatile.Read(ref _disposed))
        {
            return false;
        }

        Interlocked.Increment(ref _posted);
        if (_observations.Writer.TryWrite(observation))
        {
            return true;
        }

        Interlocked.Decrement(ref _posted);
        Interlocked.Increment(ref _dropped);
        return false;
    }

    /// <summary>
    /// Completes once every observation posted before this call has been handled.
    /// Lets tests synchronize on the consumer instead of sleeping.
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

            if (Volatile.Read(ref _processed) >= Volatile.Read(ref _posted))
            {
                return;
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

    private async Task ConsumeAsync()
    {
        while (await _observations.Reader.WaitToReadAsync().ConfigureAwait(false))
        {
            while (_observations.Reader.TryRead(out SessionTelemetryObservation observation))
            {
                try
                {
                    await _handler(observation).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    _logger.LogError(
                        exception,
                        "Session telemetry observation {Kind} for port {PortId} failed.",
                        observation.Kind,
                        observation.PortId);
                }
                finally
                {
                    Interlocked.Increment(ref _processed);
                    SignalIdle();
                }
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _observations.Writer.TryComplete();
        await _consumerTask.ConfigureAwait(false);
    }
}
