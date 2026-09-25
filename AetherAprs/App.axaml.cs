// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later
using AetherAprs.Factories;
using AetherAprs.Services;
using AetherAprs.Services.Bluetooth;
using AetherAprs.Transports.Kiss;
using AetherAprs.ViewModels;
using AetherAprs.Views;
using AetherAprs.Views.Windows;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading.Tasks;
using AetherAprs.Data;

namespace AetherAprs;

public partial class App : Application
{
    // Ignore the warning about the property being non-nullable, as it will
    // be initialized in OnFrameworkInitializationCompleted.
    public IServiceProvider ServiceProvider { get; private set; } = null!;

    /// <summary>
    /// This method is intended to be overridden in platform-specific
    /// implementations of the App class to register platform-specific services.
    /// </summary>
    /// <param name="services">The service collection to register services in.</param>
    protected virtual void OverrideCoreServices(IServiceCollection services)
    {
        // This method can be overridden in platform-specific implementations of
        // the App class to override core services with platform-specific
        // implementations.
    }

    protected virtual void RegisterPlatformServices(IServiceCollection services)
    {
        // Register default implementation of IAppDataDirProviderService for desktop/core platforms
        services.AddSingleton<Services.IAppDataDirProviderService, Services.AppDataDirProviderService>();
        services.AddSingleton<IUiCultureProvider, OsUiCultureProvider>();

        // Desktop has no BLE/SPP stack yet — register unsupported placeholders.
        services.AddSingleton<IKissStreamConnector, UnsupportedBluetoothClassicKissStreamConnector>();
        services.AddSingleton<IKissStreamConnector, UnsupportedBluetoothLeKissStreamConnector>();
        services.AddSingleton<IBluetoothLeScanner, UnsupportedBluetoothLeScanner>();
        services.AddSingleton<IBluetoothClassicDeviceProvider, UnsupportedBluetoothClassicDeviceProvider>();
    }

    /// <summary>
    /// Gets a service from the application's service provider.
    /// </summary>
    public static T GetService<T>() where T : notnull
    {
        var app = Current as App;
        return (T)(app?.ServiceProvider.GetRequiredService(typeof(T)) ?? throw new InvalidOperationException("Service provider not initialized."));
    }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
#if DEBUG
        try
        {
            this.AttachDeveloperTools();
        }
        catch (Exception ex)
        {
            // Developer tools may fail to connect on some platforms (e.g., Android).
            // This is not critical, so we just log and continue.
            Console.WriteLine($"Failed to attach developer tools: {ex.Message}");
        }
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
        ServiceProvider = ServiceProviderFactory.CreateServiceProvider(RegisterPlatformServices, OverrideCoreServices);

        Localization.UiCulture.Apply(ServiceProvider.GetRequiredService<IUiCultureProvider>().GetUiCulture());

        ServiceProvider.GetRequiredService<AppSavedDataInitializer>().Initialize();

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

        _ = StartEnabledPortsAsync();

        base.OnFrameworkInitializationCompleted();
    }

    private async Task StartEnabledPortsAsync()
    {
        try
        {
            await ServiceProvider.GetRequiredService<IPortService>().StartAllEnabledPortsAsync();
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Failed to start enabled ports: {exception}");
        }
    }
}
