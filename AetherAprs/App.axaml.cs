// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later
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
    private ServiceProvider? _serviceProvider;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        // Services - singletons for shared state
        services.AddSingleton<IConfigurationService, ConfigurationService>();
        services.AddSingleton<ILoggingService, LoggingService>();
        services.AddSingleton<INavigationFactory, NavigationFactory>();
        services.AddSingleton<INavigationService, NavigationService>();

        // Main view model - singleton because it owns the navigation state
        services.AddSingleton<MainViewModel>();

        // Page view models - transient so each navigation creates a fresh instance
        services.AddTransient<HomeViewModel>();

        // Views - transient, created on demand
        services.AddTransient<MainWindow>();
    }

    private static MainView CreateMainView(IServiceProvider services)
    {
        // Single-view platforms require manual view creation with ViewModel assignment.
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
        var services = new ServiceCollection();
        ConfigureServices(services);
        _serviceProvider = services.BuildServiceProvider();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = _serviceProvider.GetRequiredService<MainWindow>();
            desktop.ShutdownRequested += OnShutdownRequested;
        }
        else if (ApplicationLifetime is IActivityApplicationLifetime activityLifetime)
        {
            InitializeViewBasedPlatform(
                _serviceProvider,
                view => activityLifetime.MainViewFactory = () => view);
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewPlatform)
        {
            InitializeViewBasedPlatform(
                _serviceProvider,
                view => singleViewPlatform.MainView = view);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void OnShutdownRequested(object? sender, ShutdownRequestedEventArgs e)
    {
        _serviceProvider?.Dispose();
        _serviceProvider = null;
    }
}
