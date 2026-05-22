// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later
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

    internal static void ConfigureServices(IServiceCollection services)
    {
        // Services - singletons for shared state
        services.AddSingleton<ILoggingService, LoggingService>();
        services.AddSingleton<INavigationFactory, NavigationFactory>();
        services.AddSingleton<INavigationService, NavigationService>();

        // Main view model - singleton because it owns the navigation state
        services.AddSingleton<MainViewModel>();

        // Page view models - transient so each navigation creates a fresh instance
        services.AddTransient<HomeViewModel>();
        services.AddTransient<SettingsViewModel>();
    }

    protected virtual void RegisterPlatformServices(IServiceCollection services)
    {
    }

    private MainView CreateMainView()
    {
        // Single-view platforms require manual view creation with ViewModel assignment.
        return new MainView
        {
            DataContext = _serviceProvider!.GetRequiredService<MainViewModel>()
        };
    }

    private void SetupMobileBackHooks(TopLevel? topLevel)
    {
        if (topLevel is null) return;
        var nav = _serviceProvider!.GetRequiredService<INavigationService>();

        topLevel.BackRequested += (s, e) =>
        {
            if (nav.CanGoBack)
            {
                nav.GoBack();
                e.Handled = true;
            }
        };
    }

    private void InitializeViewBasedPlatform(
        Action<MainView> assignMainView)
    {
        var view = CreateMainView();

        assignMainView(view);
        SetupMobileBackHooks(TopLevel.GetTopLevel(view));
    }

    public override void OnFrameworkInitializationCompleted()
    {
        var services = new ServiceCollection();
        ConfigureServices(services);
        RegisterPlatformServices(services);
        _serviceProvider = services.BuildServiceProvider();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow { DataContext = _serviceProvider.GetRequiredService<MainViewModel>() };
            desktop.ShutdownRequested += OnShutdownRequested;
        }
        else if (ApplicationLifetime is IActivityApplicationLifetime activityLifetime)
        {
            InitializeViewBasedPlatform(
                view => activityLifetime.MainViewFactory = () => view);
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewPlatform)
        {
            InitializeViewBasedPlatform(
                view => singleViewPlatform.MainView = view);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void OnShutdownRequested(object? sender, ShutdownRequestedEventArgs e)
    {
        _serviceProvider?.Dispose();
    }
}
