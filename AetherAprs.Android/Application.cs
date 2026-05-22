// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later
using Android.App;
using Android.Runtime;
using Avalonia;
using Avalonia.Android;
using Microsoft.Extensions.DependencyInjection;

namespace AetherAprs.Android
{
    public class AndroidApp : App
    {
        protected override void RegisterPlatformServices(IServiceCollection services)
        {
            services.AddSingleton<Services.Configuration.AndroidConfigurationService>();
            services.AddSingleton<AetherAprs.Services.Configuration.IConfigurationService>(
                provider => provider.GetRequiredService<Services.Configuration.AndroidConfigurationService>());
        }
    }

    [Application]
    public class Application : AvaloniaAndroidApplication<AndroidApp>
    {
        protected Application(nint javaReference, JniHandleOwnership transfer) : base(javaReference, transfer)
        {
        }

        protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
        {
            return base.CustomizeAppBuilder(builder)
            .WithInterFont();
        }
    }
}

