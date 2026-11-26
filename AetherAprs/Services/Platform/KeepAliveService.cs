// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Services.Diagnostics;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AetherAprs.Services.Platform;

/// <summary>
/// Default <see cref="IKeepAliveService"/> implementation. Tracks the set of held
/// keep-alive reasons and drives the platform <see cref="IForegroundService"/>
/// so it runs exactly while at least one reason is held.
/// </summary>
/// <remarks>
/// Every transition runs under one gate, including the platform call, so the
/// platform never sees a stop while a start is still in flight and the reported
/// state always matches the last completed transition. Because the gate spans
/// the platform call, an acquire that is slow to start delays a concurrent
/// release instead of letting both run at once.
/// </remarks>
public sealed class KeepAliveService : IKeepAliveService, IDisposable
{
    private readonly IForegroundService _foregroundService;
    private readonly ILogger<KeepAliveService> _logger;
    private readonly IDiagnosticEventSink? _diagnostics;
    private readonly SemaphoreSlim _transitionGate = new(1, 1);
    private readonly HashSet<KeepAliveReason> _heldReasons = [];

    private KeepAliveStatus _status = KeepAliveStatus.Stopped(ForegroundServiceType.None);
    private bool _disposed;

    public KeepAliveService(
        IForegroundService foregroundService,
        ILogger<KeepAliveService> logger,
        IDiagnosticEventSink? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(foregroundService);
        ArgumentNullException.ThrowIfNull(logger);

        _foregroundService = foregroundService;
        _logger = logger;
        _diagnostics = diagnostics;
    }

    /// <inheritdoc />
    public KeepAliveStatus Status => Volatile.Read(ref _status);

    /// <inheritdoc />
    public event EventHandler<KeepAliveStatusChangedEventArgs>? StatusChanged;

    /// <inheritdoc />
    public async Task<KeepAliveStatus> AcquireAsync(KeepAliveReason reason)
    {
        await _transitionGate.WaitAsync().ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            // A repeated acquire keeps its reason held and still reconciles, so a
            // hold that degraded can recover on the next attempt.
            if (!_heldReasons.Add(reason))
            {
                _logger.LogDebug("Keep-alive reason {Reason} is already held.", reason);
            }

            return await ReconcileAsync().ConfigureAwait(false);
        }
        finally
        {
            _transitionGate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<KeepAliveStatus> ReleaseAsync(KeepAliveReason reason)
    {
        await _transitionGate.WaitAsync().ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (!_heldReasons.Remove(reason))
            {
                _logger.LogDebug("Keep-alive reason {Reason} was not held.", reason);
            }

            return await ReconcileAsync().ConfigureAwait(false);
        }
        finally
        {
            _transitionGate.Release();
        }
    }

    /// <summary>
    /// Applies the held reasons to the platform. Callers must hold
    /// <see cref="_transitionGate"/>.
    /// </summary>
    private async Task<KeepAliveStatus> ReconcileAsync()
    {
        var requiredTypes = GetRequiredTypes();

        if (_heldReasons.Count == 0)
        {
            return await StopAsync().ConfigureAwait(false);
        }

        if (_status.State == KeepAliveState.Active && _status.RequiredTypes == requiredTypes)
        {
            return _status;
        }

        var refresh = _status.State == KeepAliveState.Active;

        ForegroundServiceStartResult result;
        try
        {
            result = await _foregroundService.StartAsync(requiredTypes).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "The platform foreground service could not be started.");
            return Publish(KeepAliveStatus.Degraded(
                _heldReasons,
                requiredTypes,
                $"The foreground service could not be started: {exception.Message}"));
        }

        if (!result.IsRunning)
        {
            var reason = result.FailureReason ?? "The foreground service is not running.";
            _logger.LogWarning(
                "Keep-alive is degraded for {Types}: {Reason}",
                requiredTypes,
                reason);
            return Publish(KeepAliveStatus.Degraded(_heldReasons, requiredTypes, reason));
        }

        if (refresh)
        {
            _logger.LogInformation(
                "Refreshed the keep-alive foreground service types to {Types}.",
                requiredTypes);
        }
        else
        {
            _logger.LogInformation(
                "Started the keep-alive foreground service for {Types}.",
                requiredTypes);
        }

        return Publish(KeepAliveStatus.Active(_heldReasons, requiredTypes));
    }

    /// <summary>
    /// Stops the platform service and reports the outcome. Callers must hold
    /// <see cref="_transitionGate"/>.
    /// </summary>
    private async Task<KeepAliveStatus> StopAsync()
    {
        if (_status.State == KeepAliveState.Stopped)
        {
            return _status;
        }

        // Report the transition before asking the platform, so an observer never
        // sees "Stopped" for a service that is still running.
        Publish(KeepAliveStatus.Stopping([], ForegroundServiceType.None));

        try
        {
            await _foregroundService.StopAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "The platform foreground service could not be stopped.");
            return Publish(KeepAliveStatus.Degraded(
                [],
                ForegroundServiceType.None,
                $"The foreground service could not be stopped: {exception.Message}"));
        }

        if (_foregroundService.IsRunning)
        {
            const string reason = "The foreground service is still running after a stop request.";
            _logger.LogWarning("{Reason}", reason);
            return Publish(KeepAliveStatus.Degraded([], ForegroundServiceType.None, reason));
        }

        _logger.LogInformation("Stopped the keep-alive foreground service.");
        return Publish(KeepAliveStatus.Stopped(ForegroundServiceType.None));
    }

    /// <summary>
    /// Stores and reports a new state. Callers must hold
    /// <see cref="_transitionGate"/>.
    /// </summary>
    private KeepAliveStatus Publish(KeepAliveStatus status)
    {
        var previous = _status;
        Volatile.Write(ref _status, status);

        if (previous.DescribesSameHold(status))
        {
            return status;
        }

        _diagnostics?.Record(
            "keepalive",
            status.State == KeepAliveState.Degraded
                ? DiagnosticSeverity.Warning
                : DiagnosticSeverity.Information,
            status.State.ToString(),
            Describe(status));

        StatusChanged?.Invoke(this, new KeepAliveStatusChangedEventArgs { Status = status });
        return status;
    }

    private ForegroundServiceType GetRequiredTypes()
    {
        var types = ForegroundServiceType.None;

        if (_heldReasons.Contains(KeepAliveReason.Ports))
        {
            types |= ForegroundServiceType.ConnectedDevice;
        }

        if (_heldReasons.Contains(KeepAliveReason.LocationTracking))
        {
            types |= ForegroundServiceType.Location;
        }

        return types;
    }

    private static string Describe(KeepAliveStatus status)
    {
        if (status.DegradedReason is not null)
        {
            return status.DegradedReason;
        }

        return status.Reasons.Count == 0
            ? "No keep-alive reason is held."
            : $"Hold reasons: {string.Join(", ", status.Reasons.Select(reason => reason.ToString()))}.";
    }

    /// <summary>
    /// Marks the service unusable at container teardown. The gate is not disposed
    /// because a transition may still be inside it; it holds no unmanaged state
    /// unless a waiter asks for its kernel handle, which this type never does.
    /// </summary>
    public void Dispose()
    {
        _disposed = true;
    }
}
