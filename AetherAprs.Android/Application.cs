// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later
using Android.App;
using Android.Runtime;
using Avalonia.Android;
using AetherAprs.Android.Services.Bluetooth;
using AetherAprs.Android.Services.Platform;
using AetherAprs.Android.Services.Transports;
using AetherAprs.Factories;
using AetherAprs.Services.Bluetooth;
using AetherAprs.Transports.Kiss;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.IO;

namespace AetherAprs.Android;

public class AndroidApp : App
{
    /// <summary>
    /// Supplies the Android app-data directories before the container exists.
    /// </summary>
    /// <returns>The Android app-data provider.</returns>
    protected override AetherAprs.Services.Platform.IAppDataDirProviderService CreateAppDataDirProvider() =>
        new AppDataDirProviderService();

    protected override void RegisterPlatformServices(IServiceCollection services)
    {
        // Additive registrations only. Anything that replaces a core service
        // belongs in OverrideCoreServices so the replacement is explicit.
        services.AddSingleton(AndroidLifecycleTelemetry.State);

        // Observes AudioManager route/focus changes and feeds the shared monitor
        // that the PTT and playback paths authorize against.
        services.AddSingleton<AudioRoutePlatformSource>();

        // Bluetooth Classic SPP + BLE KISS transports and device discovery.
        // Connectors accumulate, so every transport stays available.
        services.AddSingleton<IKissStreamConnector, BluetoothClassicKissStreamConnector>();
        services.AddSingleton<IKissStreamConnector, BluetoothLeKissStreamConnector>();
    }

    protected override void OverrideCoreServices(CoreServiceOverrides serviceOverrides)
    {
        // Android implements these core services itself. Replacement is
        // explicit, so a core registration that disappears fails here instead
        // of silently falling back to the no-op default.
        serviceOverrides.Replace<AetherAprs.Services.Platform.ISecretProtector, AndroidKeystoreSecretProtector>();
        serviceOverrides.Replace<AetherAprs.Services.UI.IUiCultureProvider, AndroidUiCultureProvider>();
        serviceOverrides.Replace<IBluetoothLeScanner, AndroidBluetoothLeScanner>();
        serviceOverrides.Replace<IBluetoothClassicDeviceProvider, AndroidBluetoothClassicDeviceProvider>();

        serviceOverrides.Replace<AetherAprs.Services.Platform.ILocationService, LocationService>();
        serviceOverrides.Replace<AetherAprs.Services.Platform.IPermissionService, AndroidPermissionService>();
        serviceOverrides.Replace<AetherAprs.Services.Platform.IForegroundService, AndroidForegroundService>();
        serviceOverrides.Replace<AetherAprs.Services.Platform.ICompassService, CompassService>();
        serviceOverrides.Replace<AetherAprs.Services.Platform.INotificationService, AndroidNotificationService>();
        serviceOverrides.Replace<AetherAprs.Services.Platform.IAudioStreamService, AudioStreamService>();
        serviceOverrides.Replace<AetherAprs.Services.Platform.IAudioPlaybackService, AudioPlaybackService>();
        serviceOverrides.Replace<AetherAprs.Services.Platform.IPttService, PttService>();
    }
}

[Application]
public class Application : AvaloniaAndroidApplication<AndroidApp>
{
    // Android constructs this class, so it cannot take an injected logger.
    private static readonly ILogger _logger = ApplicationLog.For<Application>();

    private static readonly string _appSettingsFileName = "appsettings.json";
#if DEBUG
    private static readonly string _appSettingsDevelopmentFileName = "appsettings.Development.json";
#endif

    protected Application(nint javaReference, JniHandleOwnership transfer) : base(javaReference, transfer)
    {
    }

    public override void OnCreate()
    {
        // Ensure configuration files exist before Avalonia initializes
        EnsureConfigurationFiles();

        base.OnCreate();

        Localization.UiCulture.Apply(new AetherAprs.Android.Services.Platform.AndroidUiCultureProvider().GetUiCulture());
    }

    private static void EnsureConfigurationFiles()
    {
        try
        {
            // Use the AppDataDirProviderService to get the directory
            AppDataDirProviderService appDataDirProvider = new();
            string appDataDir = appDataDirProvider.GetAppDataDirectory();

            // Ensure directory exists
            if (!Directory.Exists(appDataDir))
            {
                Directory.CreateDirectory(appDataDir);
            }

            // Extract base configuration file if it doesn't exist
            ExtractConfigFile(appDataDir, _appSettingsFileName);

#if DEBUG
            // Extract Development configuration in DEBUG builds if it doesn't exist
            ExtractConfigFile(appDataDir, _appSettingsDevelopmentFileName);
#endif
        }
        catch (System.Exception ex)
        {
            // Log before rethrowing; without configuration files, app cannot start.
            _logger.LogCritical(ex, "Configuration initialization failed; startup cannot continue");
            throw;
        }
    }

    private static void ExtractConfigFile(string targetDirectory, string fileName)
    {
        string targetPath = Path.Combine(targetDirectory, fileName);

        // Only extract if the file doesn't already exist (preserve user settings)
        if (!File.Exists(targetPath))
        {
            try
            {
                using Stream stream = (Context?.Assets?.Open(fileName)) ?? throw new System.InvalidOperationException($"Asset {fileName} not found in APK");
                using FileStream fileStream = File.Create(targetPath);
                stream.CopyTo(fileStream);
                fileStream.Flush();
            }
            catch (System.Exception ex)
            {
                throw new System.InvalidOperationException($"Failed to extract {fileName} to {targetPath}", ex);
            }
        }
    }
}
