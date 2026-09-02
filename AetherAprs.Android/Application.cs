// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later
using Android.App;
using Android.Runtime;
using Avalonia;
using Avalonia.Android;
using Microsoft.Extensions.DependencyInjection;
using System.IO;
using System.Linq;

namespace AetherAprs.Android
{
    public class AndroidApp : App
    {
        protected override void ConfigurePlatformServices(IServiceCollection services)
        {
            // Register Android-specific implementation of IAppDataDirProviderService
            services.AddSingleton<AetherAprs.Services.IAppDataDirProviderService, Services.AppDataDirProviderService>();
        }
    }

    [Application]
    public class Application : AvaloniaAndroidApplication<AndroidApp>
    {
        private static readonly string appSettingsFileName = "appsettings.json";
        private static readonly string appSettingsDevelopmentFileName = "appsettings.Development.json";

        protected Application(nint javaReference, JniHandleOwnership transfer) : base(javaReference, transfer)
        {
        }

        public override void OnCreate()
        {
            base.OnCreate();

            // Ensure configuration files exist before Avalonia initializes
            EnsureConfigurationFiles();
        }

        private static void EnsureConfigurationFiles()
        {
            // Use the AppDataDirProviderService to get the directory
            var appDataDirProvider = new Services.AppDataDirProviderService();
            var appDataDir = appDataDirProvider.GetAppDataDirectory();

            // Always extract base configuration file
            ExtractConfigFile(appDataDir, appSettingsFileName);

#if DEBUG
            // Only extract Development configuration in DEBUG builds
            ExtractConfigFile(appDataDir, appSettingsDevelopmentFileName);
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
