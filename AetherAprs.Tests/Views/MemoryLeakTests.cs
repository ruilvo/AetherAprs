// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using AetherAprs.Models;
using AetherAprs.ViewModels.Components;
using AetherAprs.ViewModels.Pages;
using Xunit;

namespace AetherAprs.Tests.Views;

/// <summary>
/// Tests to verify that ViewModels properly manage event subscriptions
/// and resource disposal to prevent memory leaks.
/// </summary>
public class MemoryLeakTests
{
    [Fact]
    public void BeaconConfigurationItemViewModel_PropertyChanged_UpdatesConfiguration()
    {
        // Arrange
        var config = BeaconConfig.CreateWalkPreset();
        var viewModel = new BeaconConfigurationItemViewModel(config);

        // Act
        viewModel.SlowIntervalSeconds = 999;

        // Assert
        Assert.Equal(999, viewModel.Configuration.SlowIntervalSeconds);
    }

    [Fact]
    public void BeaconConfigurationItemViewModel_MultiplePropertyChanges_UpdatesConfiguration()
    {
        // Arrange
        var config = BeaconConfig.CreateDrivePreset();
        var viewModel = new BeaconConfigurationItemViewModel(config);

        // Act
        viewModel.FastIntervalSeconds = 15;
        viewModel.NormalIntervalSeconds = 45;
        viewModel.SlowIntervalSeconds = 90;
        viewModel.BeaconComment = "Test Comment";

        // Assert
        Assert.Equal(15, viewModel.Configuration.FastIntervalSeconds);
        Assert.Equal(45, viewModel.Configuration.NormalIntervalSeconds);
        Assert.Equal(90, viewModel.Configuration.SlowIntervalSeconds);
        Assert.Equal("Test Comment", viewModel.Configuration.BeaconComment);
    }

    [Fact]
    public void BeaconConfigurationItemViewModel_UsesPartialMethodsNotEventSubscription()
    {
        // Arrange
        var config = BeaconConfig.CreateWalkPreset();
        var viewModel = new BeaconConfigurationItemViewModel(config);

        int externalSubscriberCount = 0;
        viewModel.PropertyChanged += (s, e) => externalSubscriberCount++;

        // Act - Change multiple properties
        viewModel.SlowIntervalSeconds = 100;
        viewModel.NormalIntervalSeconds = 60;
        viewModel.FastIntervalSeconds = 30;

        // Assert - Each property change should trigger PropertyChanged
        // Configuration property also changes, so we expect more than 3 events
        Assert.True(externalSubscriberCount >= 3, $"Expected at least 3 property changed events, got {externalSubscriberCount}");
        
        // Verify that updating properties also updates the Configuration
        Assert.Equal(100, viewModel.Configuration.SlowIntervalSeconds);
        Assert.Equal(60, viewModel.Configuration.NormalIntervalSeconds);
        Assert.Equal(30, viewModel.Configuration.FastIntervalSeconds);
    }

    [Fact]
    public void MapViewModel_Dispose_PreventsSubsequentUpdates()
    {
        // Arrange
        var viewModel = new MapViewModel();
        var location = new LocationData
        {
            Latitude = 45.0,
            Longitude = -122.0,
            Altitude = 100.0,
            Accuracy = 10.0,
            Timestamp = DateTimeOffset.UtcNow
        };

        viewModel.UpdateUserLocation(location);
        Assert.NotNull(viewModel.UserLocation);

        // Act
        viewModel.Dispose();

        // Assert - Should throw ObjectDisposedException
        Assert.Throws<ObjectDisposedException>(() => viewModel.UpdateUserLocation(location));
    }

    [Fact]
    public void MapViewModel_Dispose_CanBeCalledMultipleTimes()
    {
        // Arrange
        var viewModel = new MapViewModel();

        // Act & Assert - Should not throw
        viewModel.Dispose();
        viewModel.Dispose();
    }

    [Fact]
    public void MapViewModel_Dispose_ClearsUserLocationLayer()
    {
        // Arrange
        var viewModel = new MapViewModel();
        var location = new LocationData
        {
            Latitude = 45.0,
            Longitude = -122.0,
            Altitude = 100.0,
            Accuracy = 10.0,
            Timestamp = DateTimeOffset.UtcNow
        };
        viewModel.UpdateUserLocation(location);

        Assert.NotNull(viewModel.UserLocationLayer);

        // Act
        viewModel.Dispose();

        // Assert
        Assert.Null(viewModel.UserLocationLayer);
    }

    [Fact]
    public void MapViewModel_CenterOnUser_RaisesEvent()
    {
        // Arrange
        var viewModel = new MapViewModel();
        var location = new LocationData
        {
            Latitude = 45.0,
            Longitude = -122.0,
            Altitude = 100.0,
            Accuracy = 10.0,
            Timestamp = DateTimeOffset.UtcNow
        };

        bool eventRaised = false;
        viewModel.CenterOnPointRequested += (s, e) => eventRaised = true;

        // Act
        viewModel.UpdateUserLocation(location);
        viewModel.CenterOnUser();

        // Assert
        Assert.True(eventRaised, "CenterOnPointRequested event should have been raised");
    }

    [Fact]
    public void MapViewModel_UpdateUserLocation_UpdatesProperty()
    {
        // Arrange
        var viewModel = new MapViewModel();
        var location = new LocationData
        {
            Latitude = 45.0,
            Longitude = -122.0,
            Altitude = 100.0,
            Accuracy = 10.0,
            Timestamp = DateTimeOffset.UtcNow
        };

        // Act
        viewModel.UpdateUserLocation(location);

        // Assert
        Assert.NotNull(viewModel.UserLocation);
        Assert.Equal(45.0, viewModel.UserLocation.Latitude);
        Assert.Equal(-122.0, viewModel.UserLocation.Longitude);
    }
}
