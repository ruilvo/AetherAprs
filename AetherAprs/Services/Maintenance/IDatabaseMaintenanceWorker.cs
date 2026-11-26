// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Threading;
using System.Threading.Tasks;

namespace AetherAprs.Services.Maintenance;

/// <summary>
/// Runs packet retention maintenance on a deferred schedule.
/// </summary>
/// <remarks>
/// Maintenance is deferred so it never competes with startup, and it owns its own
/// cancellation so a shutdown does not have to wait for a retention sweep.
/// </remarks>
public interface IDatabaseMaintenanceWorker : IAsyncDisposable
{
    /// <summary>
    /// Gets whether the scheduled loop is active.
    /// </summary>
    bool IsRunning { get; }

    /// <summary>
    /// Gets how long the worker waits between sweeps.
    /// </summary>
    TimeSpan Interval { get; }

    /// <summary>
    /// Gets how long the worker defers the first sweep after starting.
    /// </summary>
    TimeSpan InitialDelay { get; }

    /// <summary>
    /// Starts the deferred loop. Calling it again while running has no effect.
    /// </summary>
    /// <param name="cancellationToken">Cancels the start itself, not the loop.</param>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs one retention sweep now.
    /// </summary>
    /// <param name="cancellationToken">Cancels the sweep.</param>
    Task RunOnceAsync(CancellationToken cancellationToken = default);
}
