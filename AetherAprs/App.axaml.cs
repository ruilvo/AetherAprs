// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later
using AetherAprs.Services;
using AetherAprs.Services.Configuration;
using AetherAprs.Services.Logging;
using AetherAprs.Services.Navigation;
using AetherAprs.ViewModels;
using AetherAprs.Views;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using System;

namespace AetherAprs;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        // Services
        services.AddSingleton<ILoggingService, LoggingService>();
        services.AddSingleton<IConfigurationService, ConfigurationService>();
        services.AddSingleton<INavigationFactory, NavigationFactory>();
        services.AddSingleton<INavigationService, NavigationService>();

        // Main view
        services.AddTransient<MainWindow>();

        // Main view model
        services.AddSingleton<MainViewModel>(); 

        // Page view models
        services.AddTransient<HomeViewModel>();
    }

    private static MainView CreateMainView(IServiceProvider services)
    {
        // With single view frameworks we have to go around the DI and 
        // instance the view first and give it the ViewModel manually.
        return new MainView
        {
            DataContext = services.GetRequiredService<MainViewModel>()
        };
    }

    private static void SetupMobileBackHooks(TopLevel? topLevel, INavigationService nav)
    {
        if (topLevel is null) return;

        topLevel.BackRequested += (s, e) =>
        {
            if (nav.CanGoBack)
            {
                nav.GoBack();
                e.Handled = true;
            }
        };
    }

    private static void InitializeViewBasedPlatform(
        IServiceProvider provider,
        Action<MainView> assignMainView)
    {
        var view = CreateMainView(provider);
        var nav = provider.GetRequiredService<INavigationService>();

        assignMainView(view);
        SetupMobileBackHooks(TopLevel.GetTopLevel(view), nav);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Initialise DI
        var services = new ServiceCollection();
        ConfigureServices(services);
        var provider = services.BuildServiceProvider();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = provider.GetRequiredService<MainWindow>();
        }
        else if (ApplicationLifetime is IActivityApplicationLifetime singleViewFactoryApplicationLifetime)
        {
            InitializeViewBasedPlatform(
                provider,
                view => singleViewFactoryApplicationLifetime.MainViewFactory = () => view);
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewPlatform)
        {
            InitializeViewBasedPlatform(
                provider,
                view => singleViewPlatform.MainView = view);
        }

        base.OnFrameworkInitializationCompleted();
    }
}