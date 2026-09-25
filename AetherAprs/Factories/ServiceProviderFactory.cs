// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Data;
using AetherAprs.Imaging;
using AetherAprs.Services;
using AetherAprs.Transports.Kiss;
using AetherAprs.ViewModels;
using AetherAprs.ViewModels.Components;
using AetherAprs.ViewModels.Pages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.IO;

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

        services.AddDbContextFactory<AppDbContext>((sp, options) =>
        {
            var directory = sp.GetRequiredService<IAppDataDirProviderService>().GetAppDataDirectory();
            var path = Path.Combine(directory, AppDbContext.DatabaseFileName);
            options.UseSqlite($"Data Source={path}");
        });
        services.AddSingleton<AppSavedDataInitializer>();

        // Register configuration service
        services.AddSingleton<IConfigurationService, ConfigurationService>();

        // Register navigation service
        services.AddSingleton<INavigationService, NavigationService>();

        // Register KISS transport (TCP is always available; BLE/SPP come from platform)
        services.AddSingleton<IKissStreamConnector, TcpKissStreamConnector>();
        services.AddSingleton<IKissStreamFactory, KissStreamFactory>();

        // Register port service
        services.AddSingleton<IPortService, PortService>();

        // Register APRS settings resolver
        services.AddSingleton<IAprsPortSettingsResolver, AprsPortSettingsResolver>();

        // Register beacon service
        services.AddSingleton<IBeaconService, BeaconService>();

        // Register messaging
        services.AddSingleton<IMessageService, MessageService>();
        services.AddSingleton<IPacketStorageService, PacketStorageService>();
        services.AddSingleton<IPacketCacheService, PacketCacheService>();
        services.AddSingleton<IAprsSymbolBitmapProvider, AprsSymbolBitmapProvider>();
        services.AddSingleton<AprsSymbolMapConverter>();
        services.AddSingleton<ReceivedBeaconsViewModel>();

        // Register factories
        services.AddSingleton<IAddEditPortViewModelFactory, AddEditPortViewModelFactory>();
        services.AddSingleton<IPacketDetailsViewModelFactory, PacketDetailsViewModelFactory>();
        services.AddSingleton<IConversationViewModelFactory, ConversationViewModelFactory>();

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
        services.AddSingleton<MainViewModel>(); // Application-wide navigation state
        services.AddTransient<LocationTrackingViewModel>(); // Sub-component, created per HomeViewModel
        services.AddTransient<BeaconTransmissionViewModel>(); // Sub-component, created per HomeViewModel
        services.AddTransient<MapViewModel>(); // Sub-component, created per HomeViewModel
        services.AddTransient<AprsSymbolPickerViewModel>(); // Sub-component, created per SettingsViewModel
        services.AddSingleton<HomeViewModel>(); // Main page state
        services.AddSingleton<MessagesViewModel>(); // Main page state
        services.AddSingleton<PacketsViewModel>(); // Main page state
        services.AddSingleton<PortsViewModel>(); // Main page state
        services.AddSingleton<SettingsViewModel>(); // Main page state
        services.AddTransient<DynamicBeaconingViewModel>(); // Overlay sub-page, fresh each time
        services.AddTransient<AddEditPortViewModel>(); // Overlay sub-page, fresh each time
        services.AddTransient<ConversationViewModel>(); // Overlay sub-page, fresh each time
        services.AddTransient<PacketDetailsViewModel>(); // Overlay sub-page, fresh each time

        // Allow overriding core services for testing or platform-specific
        // implementations
        overrideCoreServices(services);

        // Build the final service provider
        _serviceProvider = services.BuildServiceProvider();
        return _serviceProvider;
    }
}
