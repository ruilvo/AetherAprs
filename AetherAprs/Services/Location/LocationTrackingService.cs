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
    private readonly ILocationService _locationService;
    private readonly ILogger<LocationTrackingService> _logger;
    private CancellationTokenSource? _locationUpdateCancellation;
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

        _locationUpdateCancellation?.Cancel();
        _locationUpdateCancellation = new CancellationTokenSource();

        _isTracking = true;

        _ = RunLocationUpdateLoopAsync(_locationUpdateCancellation.Token);
        
        _logger.LogInformation("Location tracking started");
    }

    public async Task RequestPermissionAndStartTrackingAsync()
    {
        if (_isTracking)
        {
            _logger.LogWarning("Location tracking already started");
            return;
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
        _locationUpdateCancellation?.Cancel();
        _locationUpdateCancellation = null;
        _isTracking = false;
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

                    await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error getting location");

                    try
                    {
                        await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            }
        }
        finally
        {
            _isTracking = false;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        StopTracking();
        _locationUpdateCancellation?.Dispose();
    }
}
