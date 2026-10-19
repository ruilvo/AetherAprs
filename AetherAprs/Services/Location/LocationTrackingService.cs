// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Models;
using AetherAprs.Services.Platform;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace AetherAprs.Services.Location;

/// <summary>
/// Service responsible for GPS location tracking.
/// </summary>
public interface ILocationTrackingService : IDisposable
{
    /// <summary>
    /// Gets the current location, or null if not available.
    /// </summary>
    LocationData? CurrentLocation { get; }

    /// <summary>
    /// Gets whether location tracking is currently active.
    /// </summary>
    bool IsTracking { get; }

    /// <summary>
    /// Gets whether location services are available on the device.
    /// </summary>
    bool IsLocationAvailable { get; }

    /// <summary>
    /// Event raised when location is updated.
    /// </summary>
    event EventHandler<LocationData>? LocationUpdated;

    /// <summary>
    /// Starts periodic location tracking.
    /// Assumes location permission has already been granted.
    /// </summary>
    Task StartTrackingAsync();

    /// <summary>
    /// Requests location permission and starts tracking if granted.
    /// Use this method when permission status is unknown.
    /// </summary>
    Task RequestPermissionAndStartTrackingAsync();

    /// <summary>
    /// Stops location tracking.
    /// </summary>
    void StopTracking();
}

/// <summary>
/// Implementation of location tracking service.
/// </summary>
public sealed class LocationTrackingService : ILocationTrackingService
{
    private const int PollingIntervalSeconds = 5;
    private const int ErrorRetryIntervalSeconds = 10;

    private readonly ILocationService _locationService;
    private readonly ILogger<LocationTrackingService> _logger;
    private readonly Lock _stateLock = new();
    private CancellationTokenSource? _locationUpdateCancellation;
    private Task? _updateLoopTask;
    private LocationData? _currentLocation;
    private bool _isTracking;
    private bool _disposed;

    public LocationData? CurrentLocation => _currentLocation;
    public bool IsTracking => _isTracking;
    public bool IsLocationAvailable => _locationService.IsLocationAvailable();

    public event EventHandler<LocationData>? LocationUpdated;

    public LocationTrackingService(
        ILocationService locationService,
        ILogger<LocationTrackingService> logger)
    {
        _locationService = locationService;
        _logger = logger;
    }

    public async Task StartTrackingAsync()
    {
        Task? staleLoop;
        CancellationTokenSource? staleCancellation;

        lock (_stateLock)
        {
            if (_disposed)
            {
                return;
            }

            if (_isTracking)
            {
                _logger.LogWarning("Location tracking already started");
                return;
            }

            if (!_locationService.IsLocationAvailable())
            {
                _logger.LogWarning("Location services are not available");
                return;
            }

            // Capture the previous loop and CTS so they can be retired outside
            // the lock without racing the new loop's lifecycle (H3).
            staleLoop = _updateLoopTask;
            staleCancellation = _locationUpdateCancellation;

            _locationUpdateCancellation = new CancellationTokenSource();
            _updateLoopTask = RunLocationUpdateLoopAsync(_locationUpdateCancellation.Token);
            _isTracking = true;
        }

        // Give the old loop a moment to observe cancellation, then dispose its
        // CTS. The old loop must never see a disposed CTS while awaiting.
        if (staleCancellation is not null)
        {
            staleCancellation.Cancel();
            _ = Task.Run(async () =>
            {
                if (staleLoop is not null)
                {
                    try
                    {
                        await staleLoop.ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Stale location update loop ended with an error");
                    }
                }

                staleCancellation.Dispose();
            });
        }

        _logger.LogInformation("Location tracking started");
    }

    public async Task RequestPermissionAndStartTrackingAsync()
    {
        lock (_stateLock)
        {
            if (_isTracking || _disposed)
            {
                _logger.LogWarning("Location tracking already started");
                return;
            }
        }

        var hasPermission = await _locationService.RequestLocationPermissionAsync();
        if (!hasPermission)
        {
            _logger.LogWarning("Location permission denied");
            return;
        }

        await StartTrackingAsync();
    }

    public void StopTracking()
    {
        CancellationTokenSource? cancellation;
        Task? loopTask;

        lock (_stateLock)
        {
            if (_disposed)
            {
                return;
            }

            cancellation = _locationUpdateCancellation;
            loopTask = _updateLoopTask;
            _locationUpdateCancellation = null;
            _updateLoopTask = null;
            _isTracking = false;
        }

        // Cancel outside the lock; the loop disposes its own CTS on exit so we
        // never dispose a CTS that a pending Task.Delay is still using (H3).
        cancellation?.Cancel();
        _ = RetireLoopAsync(cancellation, loopTask);
    }

    private async Task RunLocationUpdateLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var location = await _locationService.GetCurrentLocationAsync(cancellationToken);

                    _currentLocation = location;
                    LocationUpdated?.Invoke(this, location);

                    _logger.LogInformation("Location updated: {Lat}, {Lon}", location.Location.Latitude, location.Location.Longitude);

                    await Task.Delay(TimeSpan.FromSeconds(PollingIntervalSeconds), cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    // Raced with disposal of a stale CTS that was already replaced;
                    // treat it as cancellation (H3).
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error getting location");

                    try
                    {
                        await Task.Delay(TimeSpan.FromSeconds(ErrorRetryIntervalSeconds), cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when tracking stops.
        }
    }

    private async Task RetireLoopAsync(CancellationTokenSource? cancellation, Task? loopTask)
    {
        if (loopTask is not null)
        {
            try
            {
                await loopTask.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Location update loop ended with an error");
            }
        }

        cancellation?.Dispose();
    }

    public void Dispose()
    {
        CancellationTokenSource? cancellation;
        Task? loopTask;

        lock (_stateLock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            cancellation = _locationUpdateCancellation;
            loopTask = _updateLoopTask;
            _locationUpdateCancellation = null;
            _updateLoopTask = null;
            _isTracking = false;
        }

        cancellation?.Cancel();
        _ = RetireLoopAsync(cancellation, loopTask);
    }
}
