// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later
using AetherAprs.Services;
using AetherAprs.ViewModels;
using AetherAprs.Views;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using System;

namespace AetherAprs;

public partial class App : Application
{
    private IServiceProvider _services = null!;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        // Infrastructure Singletons
        services.AddSingleton<INavigationFactory, NavigationFactory>();
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<MainViewModel>();

        // Window Shell Registrations
        services.AddTransient<MainWindow>();
        // services.AddTransient<MainView>(); // This view is created manually

        // FUTURE FEATURES: Register your page/popup Views and ViewModels here
        // e.g., services.AddTransient<DashboardViewModel>();
        services.AddTransient<HomeViewModel>();
    }

    private void SetupMobileBackHooks(TopLevel? topLevel)
    {
        if (topLevel == null) return;

        var nav = _services.GetRequiredService<INavigationService>();

        topLevel.BackRequested += (s, e) =>
        {
            // Intercept hardware back button press if a global modal layer is visible
            if (nav.CurrentPopup != null)
            {
                nav.ClosePopup();
                e.Handled = true;
            }
            else if (nav.CanGoBack)
            {
                nav.GoBack();
                e.Handled = true;
            }
        };
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Initialise DI
        var services = new ServiceCollection();
        ConfigureServices(services);
        _services = services.BuildServiceProvider();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = _services.GetRequiredService<MainWindow>();
        }
        else if (ApplicationLifetime is IActivityApplicationLifetime singleViewFactoryApplicationLifetime)
        {
            var view = new MainView { DataContext = _services.GetRequiredService<MainViewModel>() };
            singleViewFactoryApplicationLifetime.MainViewFactory = () => view;
            SetupMobileBackHooks(TopLevel.GetTopLevel(view));
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewPlatform)
        {
            var view = new MainView { DataContext = _services.GetRequiredService<MainViewModel>() };
            singleViewPlatform.MainView = view;
            SetupMobileBackHooks(TopLevel.GetTopLevel(view));
        }

        base.OnFrameworkInitializationCompleted();
    }
}
