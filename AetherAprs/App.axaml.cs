// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later
using AetherAprs.ViewModels;
using AetherAprs.Views;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;

namespace AetherAprs;

public partial class App : Application
{
    // Ignore the warning about the property being non-nullable, as it will
    // be initialized in OnFrameworkInitializationCompleted.
    public IServiceProvider ServiceProvider { get; private set; } = null!;

    private static void ConfigureServices(IServiceCollection services)
    {
        // Register logging
        services.AddLogging(builder =>
        {
            builder.AddDebug();
            builder.AddConsole();
        });

        // Register ViewModels
        services.AddSingleton<MainViewModel>();
    }

    /// <summary>
    /// This method is intended to be overridden in platform-specific
    /// implementations of the App class to register platform-specific services.
    /// </summary>
    /// <param name="services">The service collection to register services in.</param>
    protected virtual void ConfigurePlatformServices(IServiceCollection services)
    {
    }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
#if DEBUG
        this.AttachDeveloperTools();
#endif
    }

    private MainView CreateMainView()
    {
        return new MainView
        {
            DataContext = ServiceProvider.GetRequiredService<MainViewModel>()
        };
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Configure dependency injection
        var services = new ServiceCollection();
        ConfigureServices(services);
        ConfigurePlatformServices(services);
        ServiceProvider = services.BuildServiceProvider();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = ServiceProvider.GetRequiredService<MainViewModel>()
            };
        }
        else if (ApplicationLifetime is IActivityApplicationLifetime singleViewFactoryApplicationLifetime)
        {
            singleViewFactoryApplicationLifetime.MainViewFactory = CreateMainView;
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewPlatform)
        {
            singleViewPlatform.MainView = CreateMainView();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
