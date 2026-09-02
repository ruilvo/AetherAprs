// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later
using AetherAprs.Services;
using AetherAprs.ViewModels;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;

namespace AetherAprs.Factories;

public static class ServiceProviderFactory
{
    private static IServiceProvider? serviceProvider;

    public static IServiceProvider ServiceProvider => serviceProvider ??= CreateServiceProvider(_ =>
        throw new InvalidOperationException("A platform service configuration " +
        "must be supplied before creating the application service provider.\n" +
        "Call ServiceProviderFactory.CreateServiceProvider with a " +
        "platform -specific configuration action before accessing the " +
        "ServiceProvider property."));

    public static IServiceProvider CreateServiceProvider(Action<IServiceCollection> configurePlatformServices)
    {
        var services = new ServiceCollection();

        // Register platform-specific services first
        configurePlatformServices(services);

        // Register configuration service
        services.AddSingleton<IConfigurationService, ConfigurationService>();

        // Register logging with deferred configuration resolution
        services.AddLogging(builder =>
        {
            builder.AddDebug();
            builder.AddConsole();
        });

        // Configure logging options - this callback receives the service provider automatically
        services.AddOptions<LoggerFilterOptions>()
            .Configure<IConfigurationService>((options, configService) =>
            {
                // TODO: evaluate whether this is actually possible.
                // Does this assign operation actually work?
                options = configService.Settings.Logging;
            });

        // Register ViewModels
        services.AddSingleton<MainViewModel>();

        // Build the final service provider
        serviceProvider = services.BuildServiceProvider();
        return serviceProvider;
    }
}
