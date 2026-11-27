// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later
using AetherAprs.Configuration;
using AetherAprs.Data;
using AetherAprs.Data.MapTiles;
using AetherAprs.Factories;
using AetherAprs.Services.Configuration;
using AetherAprs.Services.Platform;
using AetherAprs.Services.Startup;
using AetherAprs.Services.UI;
using AetherAprs.ViewModels;
using AetherAprs.Views;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Mapsui.Tiling;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

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
    /// implementations of the App class to replace core service registrations.
    /// </summary>
    /// <param name="serviceOverrides">
    /// The explicit replacement protocol. A platform calls
    /// <see cref="CoreServiceOverrides.Replace{TService, TImplementation}"/> for
    /// every core service it implements itself.
    /// </param>
    protected virtual void OverrideCoreServices(CoreServiceOverrides serviceOverrides)
    {
        // This method can be overridden in platform-specific implementations of
        // the App class to replace core services with platform-specific
        // implementations.
    }

    /// <summary>
    /// This method is intended to be overridden in platform-specific
    /// implementations of the App class to add platform-specific services.
    /// </summary>
    /// <param name="services">The service collection to register services in.</param>
    protected virtual void RegisterPlatformServices(IServiceCollection services)
    {
        // This method can be overridden in platform-specific implementations of
        // the App class to register platform-specific services.
    }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
#if DEBUG
        // The AvaloniaUI.DiagnosticsSupport assembly is excluded from the Android
        // build (see AetherAprs.Android.csproj, ExcludeAssets="all"). Mono on
        // Android eagerly resolves a method's type references when JIT-compiling
        // it, so the call to AttachDeveloperTools() must live in a method that is
        // never reached (and thus never JIT-compiled) on Android/iOS — a runtime
        // check inside Initialize() itself is not enough.
        if (!OperatingSystem.IsAndroid() && !OperatingSystem.IsIOS())
        {
            AttachDeveloperToolsSafely();
        }
#endif
    }

#if DEBUG
    [MethodImpl(MethodImplOptions.NoInlining)]
    private void AttachDeveloperToolsSafely()
    {
        try
        {
            this.AttachDeveloperTools();
        }
        catch
        {
            // Developer tools may fail to connect on some platforms.
            // This is not critical, so we just continue.
        }
    }
#endif

    private MainView CreateMainView() => new()
    {
        DataContext = ServiceProvider.GetRequiredService<MainViewModel>()
    };

    /// <summary>
    /// Creates the platform bootstrap that resolves application data directories.
    /// </summary>
    /// <remarks>
    /// The container cannot be built before these paths are known: the settings
    /// file and the database live in them. Platforms with sandboxed or external
    /// storage override this to return their own provider instead of resolving one
    /// from a throwaway container.
    /// </remarks>
    /// <returns>The app-data provider for this platform.</returns>
    protected virtual IAppDataDirProviderService CreateAppDataDirProvider() =>
        new AppDataDirProviderService();

    public override void OnFrameworkInitializationCompleted()
    {
        // Configure dependency injection
        ServiceProvider = ServiceProviderFactory.CreateServiceProvider(
            CreateAppDataDirProvider(),
            RegisterPlatformServices,
            OverrideCoreServices);

        // Get logger after service provider is initialized
        _logger = ServiceProvider.GetRequiredService<ILogger<App>>();

        // Log unhandled exceptions that would otherwise vanish (fire-and-forget
        // failures outside guarded helpers, background thread crashes, etc.).
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        Localization.UiCulture.Apply(ServiceProvider.GetRequiredService<IUiCultureProvider>().GetUiCulture());

        // Apply the persisted color theme before the main view is created so the
        // correct light/dark palette is in effect from the first frame.
        ApplyRequestedThemeVariant();

        // Creating the main view reads persisted configuration, so the schema must
        // exist before the platform asks for it. The startup sequence reports this
        // step again; AppSavedDataInitializer is a one-way latch, so the second
        // call is a no-op.
        ServiceProvider.GetRequiredService<AppSavedDataInitializer>().Initialize();

        // Route the map tile cache through the application database before any
        // tile layer is created. There is no cache directory and no .nomedia
        // marker: cached tiles are rows, not image files.
        OpenStreetMap.DefaultCache = ServiceProvider.GetRequiredService<DatabaseTileCache>();

        if (ApplicationLifetime is IActivityApplicationLifetime singleViewFactoryApplicationLifetime)
        {
            singleViewFactoryApplicationLifetime.MainViewFactory = CreateMainView;
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewPlatform)
        {
            singleViewPlatform.MainView = CreateMainView();
        }

        _ = RunStartupAsync();

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Runs the ordered startup sequence and logs the outcome.
    /// </summary>
    private async Task RunStartupAsync()
    {
        try
        {
            StartupReport report = await ServiceProvider.GetRequiredService<IApplicationStartup>().RunAsync(_shutdownCts.Token);

            if (!report.Succeeded && !report.WasCancelled)
            {
                _logger?.LogWarning("Startup stopped at step {Step}.", report.Failure?.Name);
            }
        }
        catch (OperationCanceledException) when (_shutdownCts.IsCancellationRequested)
        {
            _logger?.LogInformation("Application startup cancelled during shutdown");
        }
        catch (Exception exception)
        {
            _logger?.LogError(exception, "Application startup failed");
        }
    }

    /// <summary>
    /// Applies the persisted theme mode to the application's requested theme variant.
    /// </summary>
    private void ApplyRequestedThemeVariant()
    {
        ThemeMode themeMode = ServiceProvider.GetRequiredService<IConfigurationService>().Current.Theme;
        ServiceProvider.GetRequiredService<IThemeService>().Apply(themeMode);
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
