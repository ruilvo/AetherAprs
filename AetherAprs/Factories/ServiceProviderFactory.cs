// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later
using AetherAprs.Services;
using AetherAprs.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;

namespace AetherAprs.Factories;

public static class ServiceProviderFactory
{
    private static IServiceProvider? _serviceProvider;

    public static IServiceProvider ServiceProvider => _serviceProvider ??= CreateServiceProvider(_ =>
        throw new InvalidOperationException("A platform service configuration " +
        "must be supplied before creating the application service provider.\n" +
        "Call ServiceProviderFactory.CreateServiceProvider with a " +
        "platform -specific configuration action before accessing the " +
        "ServiceProvider property."), _ => { });

    public static IServiceProvider CreateServiceProvider(Action<IServiceCollection> registerPlatformServices,
                                                      Action<IServiceCollection> overrideCoreServices)
    {
        var services = new ServiceCollection();

        // Register platform-specific services first
        registerPlatformServices(services);

        // Register configuration service
        services.AddSingleton<IConfigurationService, ConfigurationService>();

        // Register navigation service
        services.AddSingleton<INavigationService, NavigationService>();

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
                var appLoggingOptions = configService.Settings.Logging;
                // Deep copy the settings from the configuration service's
                // settings to the LoggerFilterOptions.
                options.CaptureScopes = appLoggingOptions.CaptureScopes;
                options.MinLevel = appLoggingOptions.MinLevel;
                options.Rules.Clear();
                foreach (var rule in appLoggingOptions.Rules)
                {
                    options.Rules.Add(new LoggerFilterRule(
                        rule.ProviderName,
                        rule.CategoryName,
                        rule.LogLevel,
                        rule.Filter));
                }
            });

        // Register ViewModels
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<HomeViewModel>();
        services.AddSingleton<SettingsViewModel>();

        // Allow overriding core services for testing or platform-specific
        // implementations
        overrideCoreServices(services);

        // Build the final service provider
        _serviceProvider = services.BuildServiceProvider();
        return _serviceProvider;
    }
}
