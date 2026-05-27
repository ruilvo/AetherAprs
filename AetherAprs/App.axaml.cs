// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later
using AetherAprs.Protocols;
using AetherAprs.Services.Aprs;
using AetherAprs.Services.AprsIs;
using AetherAprs.Services.Logging;
using AetherAprs.Services.Navigation;
using AetherAprs.ViewModels;
using AetherAprs.Views;
using Avalonia;
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
        services.AddSingleton<IAprsBackend, AprsIsBackend>();
        services.AddSingleton<IAprsSessionService, AprsSessionService>();

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
        return new MainView
        {
            DataContext = _serviceProvider!.GetRequiredService<MainViewModel>()
        };
    }

    public override void OnFrameworkInitializationCompleted()
    {
        var services = new ServiceCollection();
        ConfigureServices(services);
        RegisterPlatformServices(services);
        _serviceProvider = services.BuildServiceProvider();

        var log = _serviceProvider.GetRequiredService<ILoggingService>().ForContext(nameof(App));
        log.Info($"AetherAprs starting (lifetime={ApplicationLifetime?.GetType().Name ?? "(none)"})");

        try
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                log.Debug("Initialising desktop lifetime");
                desktop.MainWindow = new MainWindow { DataContext = _serviceProvider.GetRequiredService<MainViewModel>() };
                desktop.ShutdownRequested += OnShutdownRequested;
            }
            else if (ApplicationLifetime is IActivityApplicationLifetime activityLifetime)
            {
                log.Debug("Initialising activity lifetime");
                activityLifetime.MainViewFactory = CreateMainView;
            }
            else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewPlatform)
            {
                log.Debug("Initialising single-view lifetime");
                singleViewPlatform.MainView = CreateMainView();
            }
            else
            {
                log.Warn("No recognised application lifetime; main view not created");
            }
        }
        catch (Exception ex)
        {
            log.Critical($"Framework initialisation failed: {ex}");
            throw;
        }

        log.Info("Framework initialisation complete");
        base.OnFrameworkInitializationCompleted();
    }

    private void OnShutdownRequested(object? sender, ShutdownRequestedEventArgs e)
    {
        _serviceProvider?.GetService<ILoggingService>()?.ForContext(nameof(App)).Info("Shutdown requested; disposing services");
        _serviceProvider?.Dispose();
    }
}
