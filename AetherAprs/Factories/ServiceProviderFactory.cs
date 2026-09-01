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
    private static readonly Lazy<IServiceProvider> serviceProvider = new(() =>
    {
        var services = new ServiceCollection();
        ConfigureServices(services);
        return services.BuildServiceProvider();
    });

    /// <summary>
    /// Gets the configured service provider instance.
    /// This is a singleton that will be created on first access.
    /// </summary>
    public static IServiceProvider ServiceProvider => serviceProvider.Value;

    /// <summary>
    /// Configures the standard services for the application.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    public static void ConfigureServices(IServiceCollection services)
    {
        // Register configuration
        ConfigurationService configService = new();
        services.AddSingleton<IConfigurationService>(configService);
        services.AddSingleton(configService.Configuration);
        services.AddSingleton(configService.Settings);

        // Register logging
        services.AddLogging(builder =>
        {
            builder.AddDebug();
            builder.AddConsole();
            builder.AddConfiguration(configService.Configuration.GetSection("Logging"));
        });

        // Register ViewModels
        services.AddSingleton<MainViewModel>();
    }

    /// <summary>
    /// Creates a new service provider with optional additional configuration.
    /// This is useful when you need platform-specific services or custom configuration.
    /// </summary>
    /// <param name="additionalConfiguration">Optional action to configure additional services.</param>
    /// <returns>A configured service provider.</returns>
    public static IServiceProvider CreateServiceProvider(Action<IServiceCollection>? additionalConfiguration = null)
    {
        var services = new ServiceCollection();
        ConfigureServices(services);
        additionalConfiguration?.Invoke(services);
        return services.BuildServiceProvider();
    }
}
