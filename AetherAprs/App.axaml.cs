// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later
using AetherAprs.Factories;
using AetherAprs.Services.Bluetooth;
using AetherAprs.Services.Packets;
using AetherAprs.Services.Platform;
using AetherAprs.Services.Ports;
using AetherAprs.Services.UI;
using AetherAprs.Transports.Kiss;
using AetherAprs.ViewModels;
using AetherAprs.Views;
using AetherAprs.Views.Windows;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;
using AetherAprs.Data;

namespace AetherAprs;

public partial class App : Application
{
    // Ignore the warning about the property being non-nullable, as it will
    // be initialized in OnFrameworkInitializationCompleted.
    public IServiceProvider ServiceProvider { get; private set; } = null!;
    private ILogger<App>? _logger;

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
        services.AddSingleton<IAppDataDirProviderService, AppDataDirProviderService>();
        services.AddSingleton<IUiCultureProvider, OsUiCultureProvider>();
        services.AddSingleton<IPermissionService, NoOpPermissionService>();

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
        catch
        {
            // Developer tools may fail to connect on some platforms (e.g., Android).
            // This is not critical, so we just continue.
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

        // Get logger after service provider is initialized
        _logger = ServiceProvider.GetRequiredService<ILogger<App>>();

        Localization.UiCulture.Apply(ServiceProvider.GetRequiredService<IUiCultureProvider>().GetUiCulture());

        ServiceProvider.GetRequiredService<AppSavedDataInitializer>().Initialize();

        // Start database cleanup task
        _ = StartDatabaseCleanupAsync();

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
        _ = RequestPermissionsAndStartTrackingAsync();

        base.OnFrameworkInitializationCompleted();
    }

    private async Task RequestPermissionsAndStartTrackingAsync()
    {
        try
        {
            // Request notification permission first (if required by platform)
            var permissionService = ServiceProvider.GetRequiredService<IPermissionService>();
            var notificationGranted = await permissionService.RequestNotificationPermissionAsync();
            _logger?.LogInformation("Notification permission: {Granted}", notificationGranted);

            // Small delay to avoid overlapping with location permission dialog
            await Task.Delay(500);

            // Now request location permission and start tracking
            var locationService = ServiceProvider.GetRequiredService<ILocationService>();
            
            // Check if location is available
            if (!locationService.IsLocationAvailable())
            {
                _logger?.LogWarning("Location services are not available on this device");
                return;
            }

            // Request location permission
            var locationGranted = await locationService.RequestLocationPermissionAsync();
            _logger?.LogInformation("Location permission: {Granted}", locationGranted);
            
            if (!locationGranted)
            {
                _logger?.LogWarning("Location permission was denied by user");
                return;
            }

            // Start location tracking through HomeViewModel
            var homeViewModel = ServiceProvider.GetRequiredService<HomeViewModel>();
            await homeViewModel.StartLocationTrackingAsync();
            
            _logger?.LogInformation("Location tracking started successfully");
        }
        catch (Exception exception)
        {
            _logger?.LogError(exception, "Failed to request permissions and start location tracking");
        }
    }

    private async Task StartEnabledPortsAsync()
    {
        try
        {
            await ServiceProvider.GetRequiredService<IPortService>().StartAllEnabledPortsAsync();
        }
        catch (Exception exception)
        {
            _logger?.LogError(exception, "Failed to start enabled ports");
        }
    }

    private async Task StartDatabaseCleanupAsync()
    {
        try
        {
            var storageService = ServiceProvider.GetRequiredService<IPacketStorageService>();

            // Run cleanup immediately on startup
            await storageService.CleanupOldPacketsAsync();

            // Schedule periodic cleanup every 24 hours
            _ = Task.Run(async () =>
            {
                while (true)
                {
                    try
                    {
                        await Task.Delay(TimeSpan.FromHours(24));
                        await storageService.CleanupOldPacketsAsync();
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError(ex, "Database cleanup failed");
                    }
                }
            });
        }
        catch (Exception exception)
        {
            _logger?.LogError(exception, "Failed to start database cleanup");
        }
    }
}
