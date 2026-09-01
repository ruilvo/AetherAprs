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

    private static void ConfigureServices(IServiceCollection services)
    {
        // Register configuration
        ConfigurationService configService = new();
        services.AddSingleton<IConfigurationService>(configService);

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

    public static IServiceProvider CreateServiceProvider(Action<IServiceCollection> configurePlatformServices)
    {
        var services = new ServiceCollection();
        configurePlatformServices(services);
        ConfigureServices(services);
        serviceProvider = services.BuildServiceProvider();
        return serviceProvider;
    }
}
