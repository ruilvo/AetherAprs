// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later
using AetherAprs.Factories;
using AetherAprs.ViewModels;
using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using System;

namespace AetherAprs;

/// <summary>
/// Provides design-time data instances for use in XAML previews.
/// </summary>
public static class DesignData
{
    private static MainViewModel? mainViewModel;

    /// <summary>
    /// Gets the MainViewModel instance for design-time use.
    /// </summary>
    public static MainViewModel MainViewModel
    {
        get
        {
            if (mainViewModel == null)
            {
                // Check if we're in design mode
                if (Design.IsDesignMode)
                {
                    // In design mode, create a simple instance without DI
                    mainViewModel = new MainViewModel();
                }
                else
                {
                    // At runtime, use the service provider
                    mainViewModel = ServiceProviderFactory.ServiceProvider.GetRequiredService<MainViewModel>();
                }
            }
            return mainViewModel;
        }
    }
}
