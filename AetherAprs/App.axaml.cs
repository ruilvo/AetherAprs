// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later
using AetherAprs.Factories;
using AetherAprs.Services.Packets;
using AetherAprs.Services.Platform;
using AetherAprs.Services.Ports;
using AetherAprs.Services.UI;
using AetherAprs.ViewModels;
using AetherAprs.ViewModels.Pages;
using AetherAprs.Views;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using BruTile.Cache;
using Mapsui.Tiling;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AetherAprs.Data;

namespace AetherAprs;

public partial class App : Application, IDisposable
{
    // Ignore the warning about the property being non-nullable, as it will
    // be initialized in OnFrameworkInitializationCompleted.
    public IServiceProvider ServiceProvider { get; private set; } = null!;
    private ILogger<App>? _logger;
    private readonly CancellationTokenSource _shutdownCts = new();
    private bool _disposed;

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
        // This method can be overridden in platform-specific implementations of
        // the App class to register platform-specific services.
    }

    /// <summary>
    /// Gets a service from the application's service provider.
    /// </summary>
    /// <remarks>
    /// Documented exception to the "no service locator" rule (AGENTS.md):
    /// this accessor exists for code paths where constructor injection is not
    /// available — platform entry points such as the Android BroadcastReceiver
    /// (MainActivity.cs) and framework-created views. Do not use it in
    /// ViewModels, services, or views where constructor injection works.
    /// </remarks>
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

        // Log unhandled exceptions that would otherwise vanish (fire-and-forget
        // failures outside guarded helpers, background thread crashes, etc.).
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        Localization.UiCulture.Apply(ServiceProvider.GetRequiredService<IUiCultureProvider>().GetUiCulture());

        ServiceProvider.GetRequiredService<AppSavedDataInitializer>().Initialize();

        // Initialize OSM tile cache during app startup
        InitializeOsmTileCache();

        // Start database cleanup task
        _ = StartDatabaseCleanupAsync();

        if (ApplicationLifetime is IActivityApplicationLifetime singleViewFactoryApplicationLifetime)
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
            var permissionService = ServiceProvider.GetRequiredService<IPermissionService>();
            var locationService = ServiceProvider.GetRequiredService<ILocationService>();
            
            // Request all permissions (MainActivity handles actual dialogs on Android)
            _logger?.LogInformation("Requesting notification permission...");
            var notificationGranted = await permissionService.RequestNotificationPermissionAsync();
            _logger?.LogInformation("Notification permission: {Granted}", notificationGranted);
            
            _logger?.LogInformation("Requesting Bluetooth permission...");
            var bluetoothGranted = await permissionService.RequestBluetoothPermissionAsync();
            _logger?.LogInformation("Bluetooth permission: {Granted}", bluetoothGranted);
            
            _logger?.LogInformation("Requesting location permission...");
            var locationGranted = await permissionService.RequestLocationPermissionAsync();
            _logger?.LogInformation("Location permission: {Granted}", locationGranted);
            
            if (!locationGranted)
            {
                _logger?.LogWarning("Location permission was denied by user");
                return;
            }
            
            // Check if location is available
            if (!locationService.IsLocationAvailable())
            {
                _logger?.LogWarning("Location services are not available on this device");
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
                while (!_shutdownCts.Token.IsCancellationRequested)
                {
                    try
                    {
                        await Task.Delay(TimeSpan.FromHours(24), _shutdownCts.Token);
                        await storageService.CleanupOldPacketsAsync();
                    }
                    catch (OperationCanceledException)
                    {
                        // Expected during shutdown
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError(ex, "Database cleanup failed");
                    }
                }
            }, _shutdownCts.Token);
        }
        catch (Exception exception)
        {
            _logger?.LogError(exception, "Failed to start database cleanup");
        }
    }

    private void InitializeOsmTileCache()
    {
        try
        {
            var appDataDirService = ServiceProvider.GetRequiredService<IAppDataDirProviderService>();
            var appDataDir = appDataDirService.GetAppDataDirectory();
            var cacheDir = Path.Combine(appDataDir, "osm-tile-cache");
            OpenStreetMap.DefaultCache = new FileCache(cacheDir, "png");
            _logger?.LogInformation("OSM tile cache initialized at {CacheDir}", cacheDir);
        }
        catch (Exception exception)
        {
            _logger?.LogError(exception, "Failed to initialize OSM tile cache");
        }
    }

    private void OnAppDomainUnhandledException(object? sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            _logger?.LogCritical(exception, "Unhandled non-UI exception (terminating: {IsTerminating})", e.IsTerminating);
        }
        else
        {
            _logger?.LogCritical("Unhandled non-UI exception of unknown type (terminating: {IsTerminating})", e.IsTerminating);
        }
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        // Mark observed so the process is not crashed by default policy; the
        // failures are still logged for diagnostics.
        e.SetObserved();
        _logger?.LogError(e.Exception, "Unobserved task exception");
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        AppDomain.CurrentDomain.UnhandledException -= OnAppDomainUnhandledException;
        TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;

        _shutdownCts.Cancel();
        _shutdownCts.Dispose();

        // The built provider implements IDisposable; dispose hosted services
        // and other disposables registered in the container.
        if (ServiceProvider is IDisposable disposableProvider)
        {
            disposableProvider.Dispose();
        }

        GC.SuppressFinalize(this);
    }
}
