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

public partial class HomeViewModel : ViewModelBase
{
    private readonly ILocationService _locationService;
    private readonly ILogger<HomeViewModel> _logger;
    private CancellationTokenSource? _locationUpdateCancellation;

    [ObservableProperty]
    public partial LocationData? UserLocation { get; set; }

    [ObservableProperty]
    public partial bool IsLocationAvailable { get; set; }

    public HomeViewModel(ILocationService locationService, ILogger<HomeViewModel> logger)
    {
        _locationService = locationService;
        _logger = logger;
        IsLocationAvailable = _locationService.IsLocationAvailable();
    }

    public async Task StartLocationTrackingAsync()
    {
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

        // Start periodic location updates
        _ = Task.Run(async () =>
        {
            while (!_locationUpdateCancellation.Token.IsCancellationRequested)
            {
                try
                {
                    var location = await _locationService.GetCurrentLocationAsync(_locationUpdateCancellation.Token);
                    
                    UserLocation = location;

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
                    await Task.Delay(TimeSpan.FromSeconds(10), _locationUpdateCancellation.Token);
                }
            }
        }, _locationUpdateCancellation.Token);
    }

    public void StopLocationTracking()
    {
        _locationUpdateCancellation?.Cancel();
        _locationUpdateCancellation = null;
    }
}
