// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AetherAprs.Services.Location;
using AetherAprs.Services.Platform;
using AetherAprs.Services.Ports;
using Microsoft.Extensions.Logging;

namespace AetherAprs.Services.Lifecycle;

/// <summary>
/// One step of an application-session stop that did not complete.
/// </summary>
/// <param name="Step">The stable name of the stop step.</param>
/// <param name="Message">The failure message.</param>
public sealed record ApplicationSessionStopFailure(string Step, string Message);

/// <summary>
/// Outcome of an application-session stop. A failure in one step never prevents
/// the remaining steps from running.
/// </summary>
/// <param name="Failures">The steps that failed, in execution order.</param>
public sealed record ApplicationSessionStopResult(IReadOnlyList<ApplicationSessionStopFailure> Failures)
{
    /// <summary>Gets a result with no failures.</summary>
    public static ApplicationSessionStopResult Success { get; } = new([]);

    /// <summary>Gets whether every stop step completed.</summary>
    public bool Succeeded => Failures.Count == 0;
}

/// <summary>
/// Stops everything the application is running in the background.
/// </summary>
/// <remarks>
/// An in-app exit and the notification's stop action are the same operation from
/// the operator's point of view, so both go through this contract. The stop
/// disarms the operator's intent before it disconnects anything, which is what
/// stops a reconnect from starting a new session while the teardown runs.
/// </remarks>
public interface IApplicationSessionControl
{
    /// <summary>
    /// Disarms the session intent, stops every port and location worker, and
    /// releases every keep-alive reason. Repeated calls are safe.
    /// </summary>
    /// <returns>The steps that failed, if any.</returns>
    Task<ApplicationSessionStopResult> StopAsync();
}

/// <summary>
/// Default <see cref="IApplicationSessionControl"/>. It owns the order of an
/// application-session stop and nothing else.
/// </summary>
/// <remarks>
/// The order is the contract: disarm the supervisor first so no reconnect can
/// create a new modem generation, then sweep every running session, then cancel
/// location work, then release the keep-alive reasons that held the foreground
/// service. A failing step is recorded and the next one still runs: a stop that
/// gives up halfway leaves the operator with a notification they cannot dismiss.
/// </remarks>
public sealed class ApplicationSessionControl : IApplicationSessionControl, IDisposable
{
    private readonly SessionCoordinator _coordinator;
    private readonly IPortService _portService;
    private readonly ILocationTrackingService _locationTrackingService;
    private readonly IKeepAliveService _keepAliveService;
    private readonly ILogger<ApplicationSessionControl> _logger;
    private readonly SemaphoreSlim _stopGate = new(1, 1);
    private bool _disposed;

    /// <summary>
    /// Initializes a new application-session stop owner.
    /// </summary>
    /// <param name="coordinator">The session supervisor that owns the intent.</param>
    /// <param name="portService">The compatibility façade over running sessions.</param>
    /// <param name="locationTrackingService">The location worker.</param>
    /// <param name="keepAliveService">The serialized foreground-service hold.</param>
    /// <param name="logger">The logger for stop failures.</param>
    public ApplicationSessionControl(
        SessionCoordinator coordinator,
        IPortService portService,
        ILocationTrackingService locationTrackingService,
        IKeepAliveService keepAliveService,
        ILogger<ApplicationSessionControl> logger)
    {
        ArgumentNullException.ThrowIfNull(coordinator);
        ArgumentNullException.ThrowIfNull(portService);
        ArgumentNullException.ThrowIfNull(locationTrackingService);
        ArgumentNullException.ThrowIfNull(keepAliveService);
        ArgumentNullException.ThrowIfNull(logger);

        _coordinator = coordinator;
        _portService = portService;
        _locationTrackingService = locationTrackingService;
        _keepAliveService = keepAliveService;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ApplicationSessionStopResult> StopAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        List<ApplicationSessionStopFailure> failures = [];

        // A notification action and an in-app exit can arrive together. Serializing
        // the stop keeps the two disconnects and keep-alive releases from
        // interleaving, which is what makes the last release meaningful.
        await _stopGate.WaitAsync().ConfigureAwait(false);
        try
        {
            // Disarm and disconnect in one step. The coordinator rejects reconnects
            // once it is stopping, so no new session can appear while the rest of
            // the teardown runs.
            await RunStepAsync("session", _coordinator.StopAsync, failures).ConfigureAwait(false);

            // A send may have started a session implicitly, outside the
            // coordinator's armed set, so sweep every running modem as well.
            await RunStepAsync("ports", _portService.StopAllPortsAsync, failures).ConfigureAwait(false);

            // Location tracking is the other long-lived worker that holds the
            // foreground service.
            await RunStepAsync(
                "location",
                () =>
                {
                    _locationTrackingService.StopTracking();
                    return Task.CompletedTask;
                },
                failures).ConfigureAwait(false);

            // LocationTrackingService releases its own reason without waiting. The
            // explicit release makes the final state observable before the caller
            // tears the host down, and releasing an unheld reason is a no-op.
            await RunStepAsync(
                "keepalive-location",
                () => _keepAliveService.ReleaseAsync(KeepAliveReason.LocationTracking),
                failures).ConfigureAwait(false);
            await RunStepAsync(
                "keepalive-ports",
                () => _keepAliveService.ReleaseAsync(KeepAliveReason.Ports),
                failures).ConfigureAwait(false);
        }
        finally
        {
            _stopGate.Release();
        }

        return failures.Count == 0 ? ApplicationSessionStopResult.Success : new(failures);
    }

    private async Task RunStepAsync(
        string step,
        Func<Task> action,
        List<ApplicationSessionStopFailure> failures)
    {
        try
        {
            await action().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            failures.Add(new ApplicationSessionStopFailure(step, exception.Message));
            _logger.LogError(exception, "Application session stop step {Step} failed.", step);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _stopGate.Dispose();
    }
}
