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
using System.Linq;

namespace AetherAprs;

public partial class App : Application
{
    public static IServiceProvider CurrentServices { get; private set; } = null!;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        // Infrastructure Singletons
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<MainViewModel>();

        // Window Shell Registrations
        services.AddTransient<MainWindow>();
        services.AddTransient<MainView>();

        // FUTURE FEATUES: Register your page/popup Views and ViewModels here
        // e.g., services.AddTransient<DashboardViewModel>();
    }

    private void SetupMobileBackHooks()
    {
        var nav = CurrentServices.GetRequiredService<INavigationService>();

        // Use the current application lifetime to resolve the active TopLevel control context
        TopLevel? topLevel = null;
        if (ApplicationLifetime is ISingleViewApplicationLifetime singleView)
        {
            topLevel = TopLevel.GetTopLevel(singleView.MainView);
        }

        topLevel?.BackRequested += (s, e) =>
            {
                // Intercept hardware back clicks if an overlay popup is currently visible
                if (nav.CurrentView is ViewModelWithPopupBase { ActivePopup: not null } overlay)
                {
                    overlay.ClosePopup();
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
        CurrentServices = services.BuildServiceProvider();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = CurrentServices.GetRequiredService<MainWindow>();
        }
        else if (ApplicationLifetime is IActivityApplicationLifetime singleViewFactoryApplicationLifetime)
        {
            singleViewFactoryApplicationLifetime.MainViewFactory = () => CurrentServices.GetRequiredService<MainView>();
            SetupMobileBackHooks();
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewPlatform)
        {
            singleViewPlatform.MainView = CurrentServices.GetRequiredService<MainView>();
            SetupMobileBackHooks();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
