// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Models;
using AetherAprs.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace AetherAprs.ViewModels;

/// <summary>
/// ViewModel responsible for GPS location tracking.
/// </summary>
public partial class LocationTrackingViewModel : ViewModelBase, IDisposable
{
    private readonly ILocationService _locationService;
    private readonly ILogger<LocationTrackingViewModel> _logger;
    private CancellationTokenSource? _locationUpdateCancellation;
    private bool _disposed;

    [ObservableProperty]
    public partial LocationData? CurrentLocation { get; set; }

    [ObservableProperty]
    public partial bool IsLocationAvailable { get; set; }

    [ObservableProperty]
    public partial bool IsTracking { get; set; }

    /// <summary>
    /// Event raised when location is updated.
    /// </summary>
    public event EventHandler<LocationData>? LocationUpdated;

    public LocationTrackingViewModel(
        ILocationService locationService,
        ILogger<LocationTrackingViewModel> logger)
    {
        _locationService = locationService;
        _logger = logger;
        IsLocationAvailable = _locationService.IsLocationAvailable();
    }

    /// <summary>
    /// Starts periodic location tracking.
    /// </summary>
    public async Task StartTrackingAsync()
    {
        if (IsTracking)
        {
            _logger.LogWarning("Location tracking already started");
            return;
        }

        // Request permission first
        var hasPermission = await _locationService.RequestLocationPermissionAsync();
        if (!hasPermission)
        {
            _logger.LogWarning("Location permission denied");
            return;
        }

        // Cancel any existing tracking
        _locationUpdateCancellation?.Cancel();
        _locationUpdateCancellation = new CancellationTokenSource();

        IsTracking = true;

        // Start periodic location updates
        _ = Task.Run(async () =>
        {
            while (!_locationUpdateCancellation.Token.IsCancellationRequested)
            {
                try
                {
                    var location = await _locationService.GetCurrentLocationAsync(_locationUpdateCancellation.Token);

                    CurrentLocation = location;
                    LocationUpdated?.Invoke(this, location);

                    _logger.LogInformation("Location updated: {Lat}, {Lon}", location.Latitude, location.Longitude);

                    // Wait 5 seconds before next update
                    await Task.Delay(TimeSpan.FromSeconds(5), _locationUpdateCancellation.Token);
                }
                catch (OperationCanceledException)
                {
                    // Expected when cancellation is requested
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error getting location");

                    // Wait longer on error before retrying
                    try
                    {
                        await Task.Delay(TimeSpan.FromSeconds(10), _locationUpdateCancellation.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            }

            IsTracking = false;
        }, _locationUpdateCancellation.Token);
    }

    /// <summary>
    /// Stops location tracking.
    /// </summary>
    public void StopTracking()
    {
        _locationUpdateCancellation?.Cancel();
        _locationUpdateCancellation = null;
        IsTracking = false;
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
