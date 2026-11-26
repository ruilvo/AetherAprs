// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Threading;
using System.Threading.Tasks;

namespace AetherAprs.Services.Startup;

/// <summary>
/// Runs the ordered startup sequence: database initialization, settings load,
/// feature permissions, session arm and port start.
/// </summary>
public interface IApplicationStartup
{
    /// <summary>
    /// Gets whether the sequence has finished, successfully or not.
    /// </summary>
    bool HasRun { get; }

    /// <summary>
    /// Gets the report for the finished run, or <see langword="null"/> before one finishes.
    /// </summary>
    StartupReport? LastReport { get; }

    /// <summary>
    /// Raised after each step finishes.
    /// </summary>
    event EventHandler<StartupStepCompletedEventArgs>? StepCompleted;

    /// <summary>
    /// Runs the sequence once. A later call returns the first run's report instead of
    /// running again, so activity recreation in a live process never re-arms or stops
    /// the session.
    /// </summary>
    /// <param name="cancellationToken">Stops the remaining steps when signalled.</param>
    /// <returns>The report for this process.</returns>
    Task<StartupReport> RunAsync(CancellationToken cancellationToken = default);
}
