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
    private static readonly ServiceProvider serviceProvider = CreateDesignTimeServiceProvider();
    private static ServiceProvider CreateDesignTimeServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<MainViewModel>();
        services.AddTransient<HomeViewModel>();
        services.AddTransient<SettingsViewModel>();
        return services.BuildServiceProvider();
    }

    public static MainViewModel MainViewModel
    {
        get
        {
            return serviceProvider.GetRequiredService<MainViewModel>();
        }
    }
}
