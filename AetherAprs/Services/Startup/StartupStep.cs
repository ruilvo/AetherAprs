// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AetherAprs.Services.Startup;

/// <summary>
/// The outcome of one startup step.
/// </summary>
public enum StartupStepOutcome
{
    /// <summary>The step ran to completion.</summary>
    Completed,

    /// <summary>The step was never attempted because startup was cancelled or an earlier required step failed.</summary>
    Skipped,

    /// <summary>The step threw.</summary>
    Failed
}

/// <summary>
/// One stage of application startup: a name for diagnostics and the work it performs.
/// </summary>
public sealed class StartupStep
{
    /// <summary>
    /// Initializes a new startup step.
    /// </summary>
    /// <param name="name">A short, stable name used in logs, diagnostics and tests.</param>
    /// <param name="runAsync">The work to perform.</param>
    /// <param name="isRequired">
    /// When <see langword="true"/>, a failure stops startup and later steps are skipped.
    /// When <see langword="false"/>, the failure is recorded and startup continues so an
    /// optional feature cannot keep the rest of the application from running.
    /// </param>
    public StartupStep(string name, Func<CancellationToken, Task> runAsync, bool isRequired = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(runAsync);

        Name = name;
        RunAsync = runAsync;
        IsRequired = isRequired;
    }

    /// <summary>
    /// Gets the short, stable name used in logs, diagnostics and tests.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the work to perform.
    /// </summary>
    public Func<CancellationToken, Task> RunAsync { get; }

    /// <summary>
    /// Gets whether a failure stops startup.
    /// </summary>
    public bool IsRequired { get; }
}

/// <summary>
/// The result of one startup step.
/// </summary>
/// <param name="Name">The step name.</param>
/// <param name="Outcome">What happened to the step.</param>
/// <param name="Duration">How long the step took, or zero when it was skipped.</param>
/// <param name="Error">The failure, when the outcome is <see cref="StartupStepOutcome.Failed"/>.</param>
public sealed record StartupStepResult(string Name, StartupStepOutcome Outcome, TimeSpan Duration, Exception? Error)
{
    /// <summary>
    /// Gets whether the step ran to completion.
    /// </summary>
    public bool Succeeded => Outcome == StartupStepOutcome.Completed;
}

/// <summary>
/// Reports a completed startup step to observers as it finishes.
/// </summary>
/// <remarks>
/// Handlers run on the startup path, so they must not block or wait on startup itself.
/// </remarks>
public sealed class StartupStepCompletedEventArgs : EventArgs
{
    /// <summary>
    /// Gets the result of the step that just finished.
    /// </summary>
    public required StartupStepResult Result { get; init; }
}

/// <summary>
/// The ordered record of what happened during one startup run.
/// </summary>
public sealed class StartupReport
{
    private readonly StartupStepResult[] _steps;

    internal StartupReport(IReadOnlyList<StartupStepResult> steps, bool wasCancelled)
    {
        _steps = [.. steps];
        WasCancelled = wasCancelled;
    }

    /// <summary>
    /// Gets every step in declaration order, including the ones that were skipped.
    /// </summary>
    public IReadOnlyList<StartupStepResult> Steps => _steps;

    /// <summary>
    /// Gets whether the run stopped early because its cancellation token was signalled.
    /// </summary>
    public bool WasCancelled { get; }

    /// <summary>
    /// Gets whether every step completed.
    /// </summary>
    public bool Succeeded => !WasCancelled && _steps.All(step => step.Succeeded);

    /// <summary>
    /// Gets the first step that failed, or <see langword="null"/> when none did.
    /// </summary>
    public StartupStepResult? Failure =>
        _steps.FirstOrDefault(step => step.Outcome == StartupStepOutcome.Failed);
}
