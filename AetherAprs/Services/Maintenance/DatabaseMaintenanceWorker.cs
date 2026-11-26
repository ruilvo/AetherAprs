// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Threading;
using System.Threading.Tasks;
using AetherAprs.Services.Packets;
using Microsoft.Extensions.Logging;

namespace AetherAprs.Services.Maintenance;

/// <summary>
/// Owns the packet retention schedule: one deferred sweep at a time, repeatedly,
/// until the worker is disposed or the application shuts down.
/// </summary>
public sealed class DatabaseMaintenanceWorker : IDatabaseMaintenanceWorker, IDisposable
{
    /// <summary>
    /// The default time between retention sweeps.
    /// </summary>
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromHours(24);

    /// <summary>
    /// The default deferral before the first sweep.
    /// </summary>
    public static readonly TimeSpan DefaultInitialDelay = TimeSpan.FromMinutes(1);

    private readonly IPacketStorageService _storage;
    private readonly ILogger<DatabaseMaintenanceWorker> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly Lock _stateLock = new();
    private CancellationTokenSource? _cancellation;
    private Task? _loop;
    private bool _disposed;

    /// <summary>
    /// Initializes a new maintenance worker.
    /// </summary>
    /// <param name="storage">The storage service that performs the sweep.</param>
    /// <param name="logger">The logger for sweep outcomes.</param>
    /// <param name="timeProvider">The clock and delay source, overridable for tests.</param>
    /// <param name="interval">The time between sweeps.</param>
    /// <param name="initialDelay">The deferral before the first sweep.</param>
    public DatabaseMaintenanceWorker(
        IPacketStorageService storage,
        ILogger<DatabaseMaintenanceWorker> logger,
        TimeProvider? timeProvider = null,
        TimeSpan? interval = null,
        TimeSpan? initialDelay = null)
    {
        ArgumentNullException.ThrowIfNull(storage);
        ArgumentNullException.ThrowIfNull(logger);

        TimeSpan effectiveInterval = interval ?? DefaultInterval;
        TimeSpan effectiveInitialDelay = initialDelay ?? DefaultInitialDelay;
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(effectiveInterval, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(effectiveInitialDelay, TimeSpan.Zero);

        _storage = storage;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
        Interval = effectiveInterval;
        InitialDelay = effectiveInitialDelay;
    }

    /// <inheritdoc />
    public bool IsRunning
    {
        get
        {
            lock (_stateLock)
            {
                return _loop is not null;
            }
        }
    }

    /// <inheritdoc />
    public TimeSpan Interval { get; }

    /// <inheritdoc />
    public TimeSpan InitialDelay { get; }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_stateLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_loop is not null)
            {
                return Task.CompletedTask;
            }

            _cancellation = new CancellationTokenSource();
            _logger.LogInformation(
                "Packet retention maintenance starts after {InitialDelay} and repeats every {Interval}.",
                InitialDelay,
                Interval);
            _loop = RunLoopAsync(_cancellation.Token);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task RunOnceAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        try
        {
            await _storage.CleanupOldPacketsAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Packet retention maintenance completed.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // A failed sweep is retried on the next interval; it must not end the loop.
            _logger.LogError(exception, "Packet retention maintenance failed.");
        }
    }

    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(InitialDelay, _timeProvider, cancellationToken).ConfigureAwait(false);

            while (!cancellationToken.IsCancellationRequested)
            {
                await RunOnceAsync(cancellationToken).ConfigureAwait(false);
                await Task.Delay(Interval, _timeProvider, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Expected on shutdown.
        }
        finally
        {
            lock (_stateLock)
            {
                _loop = null;
            }
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        Task? loop;
        CancellationTokenSource? cancellation;

        lock (_stateLock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            loop = _loop;
            cancellation = _cancellation;
            _cancellation = null;
        }

        if (cancellation is not null)
        {
            await cancellation.CancelAsync().ConfigureAwait(false);
        }

        if (loop is not null)
        {
            try
            {
                await loop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected: the loop was asked to stop.
            }
        }

        cancellation?.Dispose();
    }

    /// <summary>
    /// Stops the schedule without waiting for the running sweep.
    /// </summary>
    /// <remarks>
    /// The container's synchronous dispose path uses this overload; the loop observes
    /// the cancelled token and ends on its own. <see cref="DisposeAsync"/> is the
    /// graceful path for callers that can await.
    /// </remarks>
    public void Dispose()
    {
        CancellationTokenSource? cancellation;

        lock (_stateLock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            cancellation = _cancellation;
            _cancellation = null;
        }

        cancellation?.Cancel();
    }
}
