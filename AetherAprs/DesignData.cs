// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later
using AetherAprs.ViewModels;
using AetherAprs.Models;
using AetherAprs.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace AetherAprs;

/// <summary>
/// Provides design-time data instances for use in XAML previews.
/// </summary>
public static class DesignData
{
    private static readonly ServiceProvider _serviceProvider = CreateDesignTimeServiceProvider();
    private static ServiceProvider CreateDesignTimeServiceProvider()
    {
        var services = new ServiceCollection();

        // Register services required by ViewModels
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<ILocationService, DesignTimeLocationService>();

        // Register logging
        services.AddLogging(builder =>
        {
            builder.AddDebug();
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Debug);
        });

        // Register view models
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<HomeViewModel>();
        services.AddSingleton<SettingsViewModel>();

        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Design-time stub implementation of ILocationService.
    /// </summary>
    private class DesignTimeLocationService : ILocationService
    {
        public bool IsLocationAvailable() => true;

        public Task<bool> RequestLocationPermissionAsync() => Task.FromResult(true);

        public Task<LocationData> GetCurrentLocationAsync(CancellationToken cancellationToken = default)
        {
            // Return a fake location (Lisbon, Portugal coordinates as example)
            return Task.FromResult(new LocationData
            {
                Latitude = 38.7223,
                Longitude = -9.1393,
                Altitude = 100,
                Accuracy = 10,
                Timestamp = DateTimeOffset.Now
            });
        }
    }

    public static MainViewModel MainViewModel
    {
        get
        {
            return _serviceProvider.GetRequiredService<MainViewModel>();
        }
    }

    public static HomeViewModel HomeViewModel
    {
        get
        {
            return _serviceProvider.GetRequiredService<HomeViewModel>();
        }
    }

    public static SettingsViewModel SettingsViewModel
    {
        get
        {
            return _serviceProvider.GetRequiredService<SettingsViewModel>();
        }
    }
}
