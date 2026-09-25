// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later
using Android.App;
using Android.Runtime;
using Avalonia;
using Avalonia.Android;
using AetherAprs.Services.Bluetooth;
using AetherAprs.Transports.Kiss;
using Microsoft.Extensions.DependencyInjection;
using System.IO;

namespace AetherAprs.Android
{
    public class AndroidApp : App
    {
        protected override void RegisterPlatformServices(IServiceCollection services)
        {
            // Register Android-specific implementation of IAppDataDirProviderService
            services.AddSingleton<AetherAprs.Services.IAppDataDirProviderService, Services.AppDataDirProviderService>();
            
            // Register Android-specific implementation of ILocationService
            services.AddSingleton<AetherAprs.Services.ILocationService, Services.LocationService>();
            services.AddSingleton<AetherAprs.Services.IUiCultureProvider, Services.AndroidUiCultureProvider>();

            // Register Android foreground service
            services.AddSingleton<AetherAprs.Services.IForegroundService, Services.AndroidForegroundService>();

            // Bluetooth Classic SPP + BLE KISS transports and device discovery
            services.AddSingleton<IKissStreamConnector, Services.BluetoothClassicKissStreamConnector>();
            services.AddSingleton<IKissStreamConnector, Services.BluetoothLeKissStreamConnector>();
            services.AddSingleton<IBluetoothLeScanner, Services.AndroidBluetoothLeScanner>();
            services.AddSingleton<IBluetoothClassicDeviceProvider, Services.AndroidBluetoothClassicDeviceProvider>();
        }

        protected override void OverrideCoreServices(IServiceCollection services)
        {
            // Add any core service overrides here if needed. For now, we don't
            // have any specific overrides for Android.
        }
    }

    [Application]
    public class Application : AvaloniaAndroidApplication<AndroidApp>
    {
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

            Localization.UiCulture.Apply(new Services.AndroidUiCultureProvider().GetUiCulture());
        }

        private static void EnsureConfigurationFiles()
        {
            // Use the AppDataDirProviderService to get the directory
            var appDataDirProvider = new Services.AppDataDirProviderService();
            var appDataDir = appDataDirProvider.GetAppDataDirectory();

            // Always extract base configuration file
            ExtractConfigFile(appDataDir, _appSettingsFileName);

#if DEBUG
            // Only extract Development configuration in DEBUG builds
            ExtractConfigFile(appDataDir, _appSettingsDevelopmentFileName);
#endif
        }

        private static void ExtractConfigFile(string targetDirectory, string fileName)
        {
            var targetPath = Path.Combine(targetDirectory, fileName);

            // Only extract if the file doesn't already exist
            if (!File.Exists(targetPath))
            {
                using var stream = Context?.Assets?.Open(fileName);
                if (stream != null)
                {
                    using var fileStream = File.Create(targetPath);
                    stream.CopyTo(fileStream);
                }
            }
        }

        protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
        {
            return base.CustomizeAppBuilder(builder)
            .WithInterFont();
        }
    }
}
