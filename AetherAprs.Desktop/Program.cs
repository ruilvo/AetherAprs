// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using Avalonia;
using Microsoft.Extensions.DependencyInjection;

namespace AetherAprs.Desktop;

public class DesktopApp : App
{
    protected override void RegisterPlatformServices(IServiceCollection services)
    {
        services.AddSingleton<Services.Configuration.DesktopConfigurationService>();
        services.AddSingleton<AetherAprs.Services.Configuration.IConfigurationService>(
            provider => provider.GetRequiredService<Services.Configuration.DesktopConfigurationService>());
    }
}

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<DesktopApp>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}

