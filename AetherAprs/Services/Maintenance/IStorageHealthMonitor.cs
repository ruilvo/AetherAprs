// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Threading;
using System.Threading.Tasks;

namespace AetherAprs.Services.Maintenance;

/// <summary>
/// Owns the free-space observation for the volume that holds local data and
/// reports it to the operator.
/// </summary>
/// <remarks>
/// The monitor is the single reader of <see cref="UI.IFreeSpaceProvider"/> for
/// the database, cache and log locations, so retention decisions and the
/// operator-facing warning always describe the same volume.
/// </remarks>
public interface IStorageHealthMonitor : IDisposable
{
    /// <summary>
    /// Gets the most recent observation, or an indeterminate snapshot when the
    /// volume has not been sampled yet.
    /// </summary>
    StorageHealthSnapshot Current { get; }

    /// <summary>
    /// Raised after <see cref="Current"/> changes to a snapshot that differs
    /// from the previously published one.
    /// </summary>
    event EventHandler<StorageHealthSnapshot>? HealthChanged;

    /// <summary>
    /// Samples the volume and publishes the result.
    /// </summary>
    /// <param name="cancellationToken">Cancels the sample.</param>
    /// <returns>The observation the sample produced.</returns>
    Task<StorageHealthSnapshot> RefreshAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts sampling on an interval until the monitor is disposed.
    /// </summary>
    /// <param name="cancellationToken">Cancels the start itself, not the schedule.</param>
    Task StartAsync(CancellationToken cancellationToken = default);
}
