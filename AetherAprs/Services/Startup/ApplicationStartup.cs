// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using AetherAprs.Services.Diagnostics;
using Microsoft.Extensions.Logging;

namespace AetherAprs.Services.Startup;

/// <summary>
/// Runs startup steps in declaration order and reports what happened.
/// </summary>
/// <remarks>
/// The sequence is deliberately stored as one ordered list instead of scattered
/// fire-and-forget tasks: the operator sees one deterministic order, a failure is
/// attributed to the step that caused it, and a required failure stops the run
/// instead of letting half the application start.
/// </remarks>
public sealed class ApplicationStartup : IApplicationStartup
{
    private readonly StartupStep[] _steps;
    private readonly ILogger<ApplicationStartup> _logger;
    private readonly IDiagnosticEventSink? _diagnostics;
    private readonly Lock _runLock = new();
    private Task<StartupReport>? _run;
    private StartupReport? _report;

    /// <summary>
    /// Initializes a new startup runner.
    /// </summary>
    /// <param name="steps">The steps, in the order they must run.</param>
    /// <param name="logger">The logger for step outcomes.</param>
    /// <param name="diagnostics">An optional sink for the support report.</param>
    public ApplicationStartup(
        IEnumerable<StartupStep> steps,
        ILogger<ApplicationStartup> logger,
        IDiagnosticEventSink? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(steps);
        ArgumentNullException.ThrowIfNull(logger);

        _steps = [.. steps];
        _logger = logger;
        _diagnostics = diagnostics;
    }

    /// <inheritdoc />
    public event EventHandler<StartupStepCompletedEventArgs>? StepCompleted;

    /// <inheritdoc />
    public bool HasRun => Volatile.Read(ref _report) is not null;

    /// <inheritdoc />
    public StartupReport? LastReport => Volatile.Read(ref _report);

    /// <inheritdoc />
    public Task<StartupReport> RunAsync(CancellationToken cancellationToken = default)
    {
        // Concurrent callers share the one run: a second activity creation must
        // observe the first run's outcome instead of arming or stopping again.
        lock (_runLock)
        {
            return _run ??= RunStepsAsync(cancellationToken);
        }
    }

    private async Task<StartupReport> RunStepsAsync(CancellationToken cancellationToken)
    {
        var results = new List<StartupStepResult>(_steps.Length);
        var cancelled = false;
        var stopped = false;

        foreach (var step in _steps)
        {
            if (stopped || cancellationToken.IsCancellationRequested)
            {
                cancelled |= cancellationToken.IsCancellationRequested;
                results.Add(new StartupStepResult(step.Name, StartupStepOutcome.Skipped, TimeSpan.Zero, null));
                continue;
            }

            var startedAt = Stopwatch.GetTimestamp();
            StartupStepResult result;

            try
            {
                await step.RunAsync(cancellationToken).ConfigureAwait(false);
                result = new StartupStepResult(
                    step.Name,
                    StartupStepOutcome.Completed,
                    Stopwatch.GetElapsedTime(startedAt),
                    null);

                _logger.LogInformation("Startup step {Step} completed.", step.Name);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                cancelled = true;
                stopped = true;
                result = new StartupStepResult(
                    step.Name,
                    StartupStepOutcome.Skipped,
                    Stopwatch.GetElapsedTime(startedAt),
                    null);
            }
            catch (Exception exception)
            {
                result = new StartupStepResult(
                    step.Name,
                    StartupStepOutcome.Failed,
                    Stopwatch.GetElapsedTime(startedAt),
                    exception);

                _logger.LogError(exception, "Startup step {Step} failed.", step.Name);
                _diagnostics?.Record(
                    "startup",
                    DiagnosticSeverity.Error,
                    "Failed",
                    $"The {step.Name} startup step failed.",
                    step.Name);

                // An optional step is a feature the operator can live without for
                // this launch; a required step failing means the rest is unsafe.
                stopped = step.IsRequired;
            }

            results.Add(result);
            RaiseStepCompleted(result);
        }

        var report = new StartupReport(results, cancelled);
        Volatile.Write(ref _report, report);

        if (report.Succeeded)
        {
            _logger.LogInformation("Application startup completed.");
        }
        else if (report.WasCancelled)
        {
            _logger.LogInformation("Application startup was cancelled before every step ran.");
        }
        else
        {
            _logger.LogWarning("Application startup did not complete.");
        }

        return report;
    }

    private void RaiseStepCompleted(StartupStepResult result)
    {
        var handler = StepCompleted;
        if (handler is null)
        {
            return;
        }

        try
        {
            handler(this, new StartupStepCompletedEventArgs { Result = result });
        }
        catch (Exception exception)
        {
            // An observer must not turn a finished step into a failed one.
            _logger.LogError(exception, "A startup step observer failed for {Step}.", result.Name);
        }
    }
}
