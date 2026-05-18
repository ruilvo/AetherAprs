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
        // services.AddTransient<MainView>(); // This view is created manually

        // FUTURE FEATURES: Register your page/popup Views and ViewModels here
        // e.g., services.AddTransient<DashboardViewModel>();
    }

    private void SetupMobileBackHooks(TopLevel? topLevel)
    {
        if (topLevel == null) return;

        var nav = CurrentServices.GetRequiredService<INavigationService>();

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
            var view = new MainView { DataContext = CurrentServices.GetRequiredService<MainViewModel>() };
            singleViewFactoryApplicationLifetime.MainViewFactory = () => view;
            SetupMobileBackHooks(TopLevel.GetTopLevel(view));
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewPlatform)
        {
            var view = new MainView { DataContext = CurrentServices.GetRequiredService<MainViewModel>() };
            singleViewPlatform.MainView = view;
            SetupMobileBackHooks(TopLevel.GetTopLevel(view));
        }

        base.OnFrameworkInitializationCompleted();
    }
}
