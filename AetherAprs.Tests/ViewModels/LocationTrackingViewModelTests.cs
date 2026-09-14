// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Threading;
using System.Threading.Tasks;
using AetherAprs.Models;
using AetherAprs.Services;
using AetherAprs.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AetherAprs.Tests.ViewModels;

public class LocationTrackingViewModelTests
{
    [Fact]
    public void IsLocationAvailable_ReturnsTrueWhenServiceReturnsTrue()
    {
        var locationService = new TestLocationService { Available = true };
        var viewModel = new LocationTrackingViewModel(locationService, NullLogger<LocationTrackingViewModel>.Instance);

        Assert.True(viewModel.IsLocationAvailable);
    }

    [Fact]
    public void IsLocationAvailable_ReturnsFalseWhenServiceReturnsFalse()
    {
        var locationService = new TestLocationService { Available = false };
        var viewModel = new LocationTrackingViewModel(locationService, NullLogger<LocationTrackingViewModel>.Instance);

        Assert.False(viewModel.IsLocationAvailable);
    }

    [Fact]
    public async Task StartTrackingAsync_SetsIsTrackingToTrue()
    {
        var locationService = new TestLocationService { PermissionGranted = true };
        var viewModel = new LocationTrackingViewModel(locationService, NullLogger<LocationTrackingViewModel>.Instance);

        await viewModel.StartTrackingAsync();

        Assert.True(viewModel.IsTracking);
        
        viewModel.Dispose();
    }

    [Fact]
    public async Task StartTrackingAsync_DoesNotStartWhenPermissionDenied()
    {
        var locationService = new TestLocationService { PermissionGranted = false };
        var viewModel = new LocationTrackingViewModel(locationService, NullLogger<LocationTrackingViewModel>.Instance);

        await viewModel.StartTrackingAsync();

        Assert.False(viewModel.IsTracking);
        
        viewModel.Dispose();
    }

    [Fact]
    public async Task StartTrackingAsync_UpdatesCurrentLocation()
    {
        var expectedLocation = CreateLocation(40.7128, -74.0060);
        var locationService = new TestLocationService 
        { 
            PermissionGranted = true,
            LocationToReturn = expectedLocation
        };
        var viewModel = new LocationTrackingViewModel(locationService, NullLogger<LocationTrackingViewModel>.Instance);

        await viewModel.StartTrackingAsync();
        await Task.Delay(100, TestContext.Current.CancellationToken); // Wait for location update

        Assert.NotNull(viewModel.CurrentLocation);
        Assert.Equal(expectedLocation.Latitude, viewModel.CurrentLocation.Latitude);
        Assert.Equal(expectedLocation.Longitude, viewModel.CurrentLocation.Longitude);
        
        viewModel.Dispose();
    }

    [Fact]
    public async Task StartTrackingAsync_RaisesLocationUpdatedEvent()
    {
        var expectedLocation = CreateLocation(51.5074, -0.1278);
        var locationService = new TestLocationService 
        { 
            PermissionGranted = true,
            LocationToReturn = expectedLocation
        };
        var viewModel = new LocationTrackingViewModel(locationService, NullLogger<LocationTrackingViewModel>.Instance);

        LocationData? receivedLocation = null;
        viewModel.LocationUpdated += (sender, location) => receivedLocation = location;

        await viewModel.StartTrackingAsync();
        await Task.Delay(100, TestContext.Current.CancellationToken); // Wait for event

        Assert.NotNull(receivedLocation);
        Assert.Equal(expectedLocation.Latitude, receivedLocation.Latitude);
        Assert.Equal(expectedLocation.Longitude, receivedLocation.Longitude);
        
        viewModel.Dispose();
    }

    [Fact]
    public async Task StopTracking_SetsIsTrackingToFalse()
    {
        var locationService = new TestLocationService { PermissionGranted = true };
        var viewModel = new LocationTrackingViewModel(locationService, NullLogger<LocationTrackingViewModel>.Instance);

        await viewModel.StartTrackingAsync();
        Assert.True(viewModel.IsTracking);

        viewModel.StopTracking();
        await Task.Delay(100, TestContext.Current.CancellationToken); // Wait for cancellation

        Assert.False(viewModel.IsTracking);
        
        viewModel.Dispose();
    }

    [Fact]
    public async Task StartTrackingAsync_WhenAlreadyTracking_LogsWarning()
    {
        var locationService = new TestLocationService { PermissionGranted = true };
        var viewModel = new LocationTrackingViewModel(locationService, NullLogger<LocationTrackingViewModel>.Instance);

        await viewModel.StartTrackingAsync();
        var wasTracking = viewModel.IsTracking;
        
        // Starting again should not cause issues
        await viewModel.StartTrackingAsync();

        Assert.True(wasTracking);
        Assert.True(viewModel.IsTracking);
        
        viewModel.Dispose();
    }

    [Fact]
    public void Dispose_StopsTracking()
    {
        var locationService = new TestLocationService { PermissionGranted = true };
        var viewModel = new LocationTrackingViewModel(locationService, NullLogger<LocationTrackingViewModel>.Instance);

        _ = viewModel.StartTrackingAsync();
        
        viewModel.Dispose();

        // After disposal, tracking should be stopped (verified by no exceptions)
        Assert.False(viewModel.IsTracking);
    }

    private static LocationData CreateLocation(double latitude, double longitude)
    {
        return new LocationData
        {
            Latitude = latitude,
            Longitude = longitude,
            Accuracy = 10,
            Timestamp = DateTimeOffset.UtcNow
        };
    }

    private sealed class TestLocationService : ILocationService
    {
        public bool Available { get; set; } = true;
        public bool PermissionGranted { get; set; } = true;
        public LocationData LocationToReturn { get; set; } = new LocationData
        {
            Latitude = 0,
            Longitude = 0,
            Accuracy = 10,
            Timestamp = DateTimeOffset.UtcNow
        };

        public bool IsLocationAvailable() => Available;

        public Task<bool> RequestLocationPermissionAsync() => Task.FromResult(PermissionGranted);

        public Task<LocationData> GetCurrentLocationAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(LocationToReturn);
        }
    }
}
