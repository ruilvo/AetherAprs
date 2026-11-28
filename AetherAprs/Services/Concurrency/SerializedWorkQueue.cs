// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

namespace AetherAprs.Services.Concurrency;

/// <summary>
/// Runs enqueued asynchronous work one item at a time, in order, and reports when
/// every item enqueued so far has finished.
/// </summary>
/// <remarks>
/// <para>
/// A timer callback cannot await, so the obvious way to run periodic asynchronous
/// work is to start a task from the callback. That is also the way to lose control
/// of it: a burst of ticks starts a burst of concurrent runs that race each other
/// over the same state, a failure disappears into an unobserved task, and disposal
/// has nothing to wait for.
/// </para>
/// <para>
/// This type keeps the scheduling synchronous — <see cref="Enqueue"/> only records
/// the work — while a single loop owns the execution. A virtual clock can therefore
/// advance the state machine deterministically, and a test can await
/// <see cref="WhenIdleAsync"/> instead of sleeping or polling.
/// </para>
/// <para>
/// A failing item is logged and the loop continues: periodic work that stops
/// forever because one iteration threw is a worse failure than a skipped pass.
/// </para>
/// </remarks>
internal sealed class SerializedWorkQueue : IAsyncDisposable, IDisposable
{
    private readonly Channel<Func<CancellationToken, Task>> _work;
    private readonly ILogger _logger;
    private readonly string _name;
    private readonly Lock _gate = new();
    private readonly CancellationTokenSource _shutdown = new();

    /// <summary>Items admitted; guarded by <see cref="_gate"/>.</summary>
    private long _queued;

    /// <summary>Items whose run finished; guarded by <see cref="_gate"/>.</summary>
    private long _completed;

    /// <summary>Signalled whenever <see cref="_completed"/> or <see cref="_queued"/> changes.</summary>
    private TaskCompletionSource _changed = CreateSignal();

    private Task? _worker;
    private bool _disposed;

    /// <summary>
    /// Initializes a new queue. The loop starts on the first
    /// <see cref="Enqueue"/>, so a service can build its queue without starting
    /// background work.
    /// </summary>
    /// <param name="name">A short, stable identifier used in failure logs.</param>
    /// <param name="logger">The logger for item failures.</param>
    public SerializedWorkQueue(string name, ILogger logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(logger);

        _name = name;
        _logger = logger;
        _work = Channel.CreateUnbounded<Func<CancellationToken, Task>>(new UnboundedChannelOptions
        {
            SingleReader = true,
            AllowSynchronousContinuations = false
        });
    }

    /// <summary>
    /// Gets how many items are still outstanding. Zero means the queue has caught
    /// up with everything enqueued so far.
    /// </summary>
    internal long Outstanding
    {
        get
        {
            lock (_gate)
            {
                return _queued - _completed;
            }
        }
    }

    /// <summary>
    /// Admits one item of work and makes sure the loop is running.
    /// </summary>
    /// <param name="work">
    /// The work to run. It must observe the token so shutdown can interrupt it.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the work was admitted, or <see langword="false"/>
    /// when the queue has already been disposed and the work was dropped.
    /// </returns>
    public bool Enqueue(Func<CancellationToken, Task> work)
    {
        ArgumentNullException.ThrowIfNull(work);

        TaskCompletionSource wake;

        lock (_gate)
        {
            if (_disposed)
            {
                return false;
            }

            _queued++;
            _worker ??= Task.Run(RunLoopAsync, CancellationToken.None);
            wake = SwapSignalLocked();
        }

        // The writer is completed by DisposeAsync under the same lock, so a write
        // here cannot fail while _disposed is false.
        _work.Writer.TryWrite(work);
        wake.TrySetResult();
        return true;
    }

    /// <summary>
    /// Completes once every item enqueued before this call has finished.
    /// </summary>
    /// <param name="cancellationToken">Abandons the wait; the work still runs.</param>
    public async Task WhenIdleAsync(CancellationToken cancellationToken = default)
    {
        while (true)
        {
            Task changed;
            lock (_gate)
            {
                if (_completed >= _queued)
                {
                    return;
                }

                changed = _changed.Task;
            }

            await changed.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task RunLoopAsync()
    {
        CancellationToken token = _shutdown.Token;

        try
        {
            await foreach (Func<CancellationToken, Task> work in _work.Reader.ReadAllAsync(token).ConfigureAwait(false))
            {
                try
                {
                    await work(token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    // Shutdown, not a failure. The item is still accounted for so a
                    // caller waiting on quiescence is released instead of hanging.
                }
                catch (Exception exception)
                {
                    _logger.LogError(
                        exception,
                        "The {WorkQueueName} work queue failed one item; the queue continues.",
                        _name);
                }

                MarkItemCompleted();
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            _logger.LogDebug("The {WorkQueueName} work queue stopped for shutdown.", _name);
        }
        finally
        {
            // Release anyone still waiting on quiescence: no further item will run.
            ReleaseWaiters();
        }
    }

    private void MarkItemCompleted()
    {
        TaskCompletionSource changed;

        lock (_gate)
        {
            _completed++;
            changed = SwapSignalLocked();
        }

        changed.TrySetResult();
    }

    /// <summary>
    /// Completes the change notification every waiter is blocked on. Callers must
    /// hold <see cref="_gate"/>; the replaced source is signalled by the caller
    /// outside the lock.
    /// </summary>
    private TaskCompletionSource SwapSignalLocked()
    {
        TaskCompletionSource replaced = _changed;
        _changed = CreateSignal();
        return replaced;
    }

    private void ReleaseWaiters()
    {
        TaskCompletionSource changed;

        lock (_gate)
        {
            _completed = _queued;
            changed = SwapSignalLocked();
        }

        changed.TrySetResult();
    }

    private static TaskCompletionSource CreateSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// Closes admission, stops the item in flight, and waits for the loop to end.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        Task? worker = Close();

        if (worker is not null)
        {
            // The loop observes cancellation and ends; a failure here was already
            // logged by the loop itself.
            await worker.ConfigureAwait(false);
        }

        _shutdown.Dispose();
    }

    /// <summary>
    /// Closes admission and stops the item in flight without waiting for it, for
    /// synchronous disposal paths that cannot await.
    /// </summary>
    public void Dispose()
    {
        Close();
        _shutdown.Dispose();
    }

    private Task? Close()
    {
        Task? worker;

        lock (_gate)
        {
            if (_disposed)
            {
                return null;
            }

            _disposed = true;
            _work.Writer.TryComplete();
            worker = _worker;
            _worker = null;
        }

        _shutdown.Cancel();
        ReleaseWaiters();
        return worker;
    }
}
