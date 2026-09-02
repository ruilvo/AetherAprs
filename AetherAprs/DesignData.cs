// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later
using AetherAprs.ViewModels;
using AetherAprs.Services;
using Microsoft.Extensions.DependencyInjection;

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

        // Register view models
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<HomeViewModel>();
        services.AddSingleton<SettingsViewModel>();

        return services.BuildServiceProvider();
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
