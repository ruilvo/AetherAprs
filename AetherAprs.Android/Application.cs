// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later
using Android.App;
using Android.Runtime;
using Avalonia;
using Avalonia.Android;
using AetherAprs.Android.Services.Bluetooth;
using AetherAprs.Android.Services.Platform;
using AetherAprs.Android.Services.Transports;
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
            services.AddSingleton<AetherAprs.Services.Platform.IAppDataDirProviderService, AppDataDirProviderService>();
            
            services.AddSingleton<AetherAprs.Services.UI.IUiCultureProvider, AndroidUiCultureProvider>();

            // Bluetooth Classic SPP + BLE KISS transports and device discovery
            services.AddSingleton<IKissStreamConnector, BluetoothClassicKissStreamConnector>();
            services.AddSingleton<IKissStreamConnector, BluetoothLeKissStreamConnector>();
            services.AddSingleton<IBluetoothLeScanner, AndroidBluetoothLeScanner>();
            services.AddSingleton<IBluetoothClassicDeviceProvider, AndroidBluetoothClassicDeviceProvider>();
        }

        protected override void OverrideCoreServices(IServiceCollection services)
        {
            // Override core services with Android-specific implementations
            
            // Replace NoOpLocationService with Android implementation
            services.AddSingleton<AetherAprs.Services.Platform.ILocationService, LocationService>();
            
            // Replace NoOpPermissionService with Android implementation
            services.AddSingleton<AetherAprs.Services.Platform.IPermissionService, AndroidPermissionService>();
            
            // Replace NoOpForegroundService with Android implementation
            services.AddSingleton<AetherAprs.Services.Platform.IForegroundService, AndroidForegroundService>();
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

            Localization.UiCulture.Apply(new AetherAprs.Android.Services.Platform.AndroidUiCultureProvider().GetUiCulture());
        }

        private static void EnsureConfigurationFiles()
        {
            try
            {
                // Use the AppDataDirProviderService to get the directory
                var appDataDirProvider = new AetherAprs.Android.Services.Platform.AppDataDirProviderService();
                var appDataDir = appDataDirProvider.GetAppDataDirectory();

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
                // Log critical failure - without config files, app cannot start
                System.Diagnostics.Debug.WriteLine($"CRITICAL: Failed to ensure configuration files: {ex}");
                throw;
            }
        }

        private static void ExtractConfigFile(string targetDirectory, string fileName)
        {
            var targetPath = Path.Combine(targetDirectory, fileName);

            // Only extract if the file doesn't already exist (preserve user settings)
            if (!File.Exists(targetPath))
            {
                try
                {
                    using var stream = Context?.Assets?.Open(fileName);
                    if (stream == null)
                    {
                        throw new System.InvalidOperationException($"Asset {fileName} not found in APK");
                    }

                    using var fileStream = File.Create(targetPath);
                    stream.CopyTo(fileStream);
                    fileStream.Flush();
                }
                catch (System.Exception ex)
                {
                    throw new System.InvalidOperationException($"Failed to extract {fileName} to {targetPath}", ex);
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
