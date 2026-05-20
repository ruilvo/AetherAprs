// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later
using AetherAprs.ViewModels;
using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;

namespace AetherAprs.Views;

public partial class MainView : UserControl
{
    public MainView()
    {
        InitializeComponent();

        if (Design.IsDesignMode)
        {
            // Steal the DI from the app to get a design-time view model instance.
            // This is a bit hacky but it works and avoids having to duplicate design-time data setup.
            var services = new ServiceCollection();
            App.ConfigureServices(services);
            var provider = services.BuildServiceProvider();
            DataContext = provider.GetRequiredService<MainViewModel>();
        }
    }
}
