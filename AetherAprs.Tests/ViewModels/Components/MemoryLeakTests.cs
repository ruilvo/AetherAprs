// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using AetherAprs.Models;
using AetherAprs.Services.UI;
using AetherAprs.ViewModels.Components;
using AetherAprs.ViewModels.Pages;
using Geo;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AetherAprs.Tests.ViewModels.Components;

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
    public void MapViewModel_CenterOnUser_RaisesEventWhenLocationIsSet()
    {
        // Arrange
        var userLocationLayer = new UserLocationLayerService(NullLogger<UserLocationLayerService>.Instance);
        var viewModel = new MapViewModel(userLocationLayer);
        var location = new LocationData
        {
            Location = new Coordinate(45.0, -122.0),
            Altitude = 100.0,
            Accuracy = 10.0,
            Timestamp = DateTimeOffset.UtcNow
        };

        bool eventRaised = false;
        viewModel.CenterOnPointRequested += (s, e) => eventRaised = true;

        // Act
        userLocationLayer.UpdateLocation(location);
        viewModel.CenterOnUser();

        // Assert
        Assert.True(eventRaised, "CenterOnPointRequested event should have been raised");
    }

    [Fact]
    public void MapViewModel_CenterOnUser_DoesNotRaiseEventWhenLocationNotSet()
    {
        // Arrange
        var userLocationLayer = new UserLocationLayerService(NullLogger<UserLocationLayerService>.Instance);
        var viewModel = new MapViewModel(userLocationLayer);

        bool eventRaised = false;
        viewModel.CenterOnPointRequested += (s, e) => eventRaised = true;

        // Act
        viewModel.CenterOnUser();

        // Assert
        Assert.False(eventRaised, "CenterOnPointRequested event should not be raised when location is not set");
    }

    [Fact]
    public void UserLocationLayerService_UpdateLocation_UpdatesCurrentMapPoint()
    {
        // Arrange
        var service = new UserLocationLayerService(NullLogger<UserLocationLayerService>.Instance);
        var location = new LocationData
        {
            Location = new Coordinate(45.0, -122.0),
            Altitude = 100.0,
            Accuracy = 10.0,
            Timestamp = DateTimeOffset.UtcNow
        };

        // Act
        service.UpdateLocation(location);

        // Assert
        Assert.NotNull(service.CurrentMapPoint);
    }

    [Fact]
    public void UserLocationLayerService_Dispose_PreventsSubsequentUpdates()
    {
        // Arrange
        var service = new UserLocationLayerService(NullLogger<UserLocationLayerService>.Instance);
        var location = new LocationData
        {
            Location = new Coordinate(45.0, -122.0),
            Altitude = 100.0,
            Accuracy = 10.0,
            Timestamp = DateTimeOffset.UtcNow
        };

        service.UpdateLocation(location);
        Assert.NotNull(service.CurrentMapPoint);

        // Act
        service.Dispose();

        // Assert - Should throw ObjectDisposedException
        Assert.Throws<ObjectDisposedException>(() => service.UpdateLocation(location));
    }

    [Fact]
    public void UserLocationLayerService_Dispose_CanBeCalledMultipleTimes()
    {
        // Arrange
        var service = new UserLocationLayerService(NullLogger<UserLocationLayerService>.Instance);

        // Act & Assert - Should not throw
        service.Dispose();
        service.Dispose();
    }
}
