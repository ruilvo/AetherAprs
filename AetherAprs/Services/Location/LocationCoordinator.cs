// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AetherAprs.Services.Location;

/// <summary>
/// Why a consumer needs the device position. The worker runs while at least one
/// purpose is held, so navigating between pages does not restart it.
/// </summary>
public enum LocationPurpose
{
    /// <summary>The map shows the station and follows it.</summary>
    Map = 0,

    /// <summary>Position beaconing needs periodic fixes.</summary>
    Beacon = 1,
}

/// <summary>
/// One position fix together with the instant it was sampled and the reported
/// accuracy, so a consumer can decide whether the fix is still usable.
/// </summary>
/// <param name="Location">The sampled position.</param>
/// <param name="SampledAt">When the platform sampled the position.</param>
/// <param name="AccuracyMeters">The reported horizontal accuracy, or null when the platform does not report one.</param>
public sealed record LocationSnapshot(LocationData Location, DateTimeOffset SampledAt, double? AccuracyMeters)
{
    /// <summary>Gets how old the fix is at <paramref name="now"/>.</summary>
    public TimeSpan AgeAt(DateTimeOffset now) => now - SampledAt;

    /// <summary>Gets whether the fix is no older than <paramref name="maximumAge"/> at <paramref name="now"/>.</summary>
    public bool IsFreshAt(DateTimeOffset now, TimeSpan maximumAge) => AgeAt(now) <= maximumAge;
}

/// <summary>
/// The single owner of the location worker. Consumers acquire a purpose while they
/// need fixes and release it when they stop observing; the worker starts on the
/// first purpose and stops after the last one is released.
/// </summary>
public interface ILocationCoordinator : IDisposable
{
    /// <summary>Gets the latest fix, or null when tracking has not produced one yet.</summary>
    LocationSnapshot? Current { get; }

    /// <summary>Gets the purposes that currently need fixes.</summary>
    IReadOnlyCollection<LocationPurpose> ActivePurposes { get; }

    /// <summary>Raised for every accepted fix.</summary>
    event EventHandler<LocationSnapshot>? LocationChanged;

    /// <summary>
    /// Acquires <paramref name="purpose"/>. Acquiring a held purpose is a no-op, so
    /// callers do not need to track whether they already asked. When this is the
    /// first purpose, the worker starts.
    /// </summary>
    /// <param name="purpose">The reason the caller needs fixes.</param>
    /// <param name="requestPermission">Ask the operator for the location permission before starting.</param>
    /// <param name="cancellationToken">Cancels the permission request or start.</param>
    Task AcquireAsync(
        LocationPurpose purpose,
        bool requestPermission = false,
        CancellationToken cancellationToken = default);

    /// <summary>Releases <paramref name="purpose"/>. Releasing a purpose that is not held is a no-op.</summary>
    void Release(LocationPurpose purpose);

    /// <summary>
    /// Stops the worker and forgets every purpose. Used by the application-session
    /// stop, where the operator ended the whole session rather than one page.
    /// </summary>
    void StopAll();

    /// <summary>
    /// Gets the latest fix only when it is no older than <paramref name="maximumAge"/>.
    /// </summary>
    bool TryGetFresh(TimeSpan maximumAge, out LocationSnapshot? snapshot);
}

/// <summary>
/// Default <see cref="ILocationCoordinator"/> over <see cref="ILocationTrackingService"/>.
/// </summary>
public sealed class LocationCoordinator : ILocationCoordinator
{
    private readonly ILocationTrackingService _trackingService;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<LocationCoordinator> _logger;
    private readonly Lock _gate = new();
    private readonly HashSet<LocationPurpose> _purposes = [];
    private LocationSnapshot? _current;
    private bool _disposed;

    public LocationCoordinator(
        ILocationTrackingService trackingService,
        TimeProvider timeProvider,
        ILogger<LocationCoordinator> logger)
    {
        ArgumentNullException.ThrowIfNull(trackingService);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _trackingService = trackingService;
        _timeProvider = timeProvider;
        _logger = logger;

        // The coordinator owns the fix stream for every consumer; purposes only decide
        // whether the worker runs. Subscribing once keeps a fix sampled by a worker
        // started elsewhere (for example by the session control) visible to pages.
        _trackingService.LocationUpdated += OnLocationUpdated;
    }

    /// <inheritdoc />
    public LocationSnapshot? Current
    {
        get
        {
            LocationSnapshot? current;
            lock (_gate)
            {
                current = _current;
            }

            // The worker is the source of truth for the fix: a caller that only reads
            // the position still sees the latest sample even before this coordinator
            // delivered it to its own subscribers.
            return current ?? ToSnapshot(_trackingService.CurrentLocation);
        }
    }

    private LocationSnapshot? ToSnapshot(LocationData? location) => location is null
        ? null
        : new LocationSnapshot(
            location,
            location.Timestamp == default ? _timeProvider.GetUtcNow() : location.Timestamp,
            location.Accuracy > 0 ? location.Accuracy : null);

    /// <inheritdoc />
    public IReadOnlyCollection<LocationPurpose> ActivePurposes
    {
        get
        {
            lock (_gate)
            {
                return [.. _purposes];
            }
        }
    }

    /// <inheritdoc />
    public event EventHandler<LocationSnapshot>? LocationChanged;

    /// <inheritdoc />
    public async Task AcquireAsync(
        LocationPurpose purpose,
        bool requestPermission = false,
        CancellationToken cancellationToken = default)
    {
        bool startWorker;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            startWorker = _purposes.Count == 0;
            _purposes.Add(purpose);
        }

        if (!startWorker)
        {
            // A held purpose already keeps the worker running; the new purpose only
            // has to survive until it releases.
            SeedFromCurrentFix();
            return;
        }

        try
        {
            if (requestPermission)
            {
                await _trackingService.RequestPermissionAndStartTrackingAsync().ConfigureAwait(false);
            }
            else
            {
                await _trackingService.StartTrackingAsync().ConfigureAwait(false);
            }

            // A fix sampled before the worker was asked to start is still the newest
            // one; publish it so the new purpose does not wait for the first poll.
            SeedFromCurrentFix();
        }
        catch
        {
            lock (_gate)
            {
                _purposes.Remove(purpose);
            }

            throw;
        }
    }

    /// <summary>
    /// A purpose acquired while the worker already runs still gets the existing fix,
    /// so a page that opens between polls does not wait for the next tick.
    /// </summary>
    private void SeedFromCurrentFix()
    {
        LocationData? location = _trackingService.CurrentLocation;
        if (location is not null)
        {
            ApplyFix(location);
        }
    }

    /// <inheritdoc />
    public void Release(LocationPurpose purpose)
    {
        bool stopWorker;
        lock (_gate)
        {
            if (!_purposes.Remove(purpose))
            {
                return;
            }

            stopWorker = _purposes.Count == 0;
        }

        // The stop runs outside the lock: the tracking service joins its loop and
        // must not call back into this coordinator while the gate is held.
        if (stopWorker)
        {
            _trackingService.StopTracking();
        }
    }

    /// <inheritdoc />
    public void StopAll()
    {
        lock (_gate)
        {
            _purposes.Clear();
        }

        // The stop is unconditional: the session stop ends tracking even when a
        // purpose set was lost, and the tracking service treats it as a no-op when
        // the worker is not running.
        _trackingService.StopTracking();
    }

    /// <inheritdoc />
    public bool TryGetFresh(TimeSpan maximumAge, out LocationSnapshot? snapshot)
    {
        LocationSnapshot? current = Current;
        if (current is not null && current.IsFreshAt(_timeProvider.GetUtcNow(), maximumAge))
        {
            snapshot = current;
            return true;
        }

        snapshot = null;
        return false;
    }

    private void OnLocationUpdated(object? sender, LocationData location) => ApplyFix(location);

    private void ApplyFix(LocationData location)
    {
        LocationSnapshot snapshot = ToSnapshot(location)!;

        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _current = snapshot;
        }

        try
        {
            LocationChanged?.Invoke(this, snapshot);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "A location subscriber failed.");
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        _trackingService.LocationUpdated -= OnLocationUpdated;
        StopAll();
    }
}
