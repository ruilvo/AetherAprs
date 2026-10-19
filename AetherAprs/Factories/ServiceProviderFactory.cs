// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Data;
using AetherAprs.Factories.Packets;
using AetherAprs.Factories.ViewModels;
using AetherAprs.Imaging;
using AetherAprs.Services.Beaconing;
using AetherAprs.Services.Configuration;
using AetherAprs.Services.Location;
using AetherAprs.Services.Messaging;
using AetherAprs.Services.Packets;
using AetherAprs.Services.Platform;
using AetherAprs.Services.Ports;
using AetherAprs.Services.Transmission;
using AetherAprs.Services.UI;
using AetherAprs.Transports.Kiss;
using AetherAprs.ViewModels;
using AetherAprs.ViewModels.Components;
using AetherAprs.ViewModels.Pages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
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

        // Register safe core defaults for platform services so that any host
        // can start without a platform-specific implementation. Platform
        // registrations below are added after these, so they override the
        // defaults (last registration wins).
        services.AddSingleton<IAppDataDirProviderService, AppDataDirProviderService>();
        services.AddSingleton<ILocationService, NoOpLocationService>();
        services.AddSingleton<IPermissionService, NoOpPermissionService>();
        services.AddSingleton<IForegroundService, NoOpForegroundService>();

        // Register platform-specific services first
        registerPlatformServices(services);

        // Get the app data directories from the platform service that was just
        // registered. The private directory hosts the database (credentials);
        // the user-accessible directory hosts configuration and caches.
        var tempProvider = services.BuildServiceProvider();
        var appDataDirService = tempProvider.GetRequiredService<IAppDataDirProviderService>();
        var appDataDirectory = appDataDirService.GetAppDataDirectory();
        var privateAppDataDirectory = appDataDirService.GetPrivateAppDataDirectory();
        tempProvider.Dispose();

        // Build configuration from files in app data directory
        var configuration = new ConfigurationBuilder()
            .SetBasePath(appDataDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
#if DEBUG
            .AddJsonFile("appsettings.Development.json", optional: true, reloadOnChange: false)
#endif
            .Build();

        // Register logging with configuration from appsettings
        services.AddLogging(builder =>
        {
            builder.AddConfiguration(configuration.GetSection("Logging"));
            builder.AddDebug();
            builder.AddConsole();
        });

        // Register database in the PRIVATE app data directory (not user-accessible):
        // the database holds APRS-IS passcodes and must not live in shared storage.
        services.AddDbContextFactory<AppDbContext>(options =>
        {
            var path = Path.Combine(privateAppDataDirectory, AppDbContext.DatabaseFileName);
            options.UseSqlite($"Data Source={path}");
        });
        services.AddSingleton<AppSavedDataInitializer>();

        // Register configuration service
        services.AddSingleton<IConfigurationService, ConfigurationService>();

        // Register navigation service
        services.AddSingleton<INavigationService, NavigationService>();

        // Register keyboard insets service
        services.AddSingleton<IKeyboardInsetsService, KeyboardInsetsService>();

        // Register UI thread dispatcher (marshals work onto the Avalonia UI thread)
        services.AddSingleton<IUiDispatcher>(AvaloniaUiDispatcher.Instance);

        // Register confirmation dialog service (shows dialogs through the main DialogHost)
        services.AddSingleton<IConfirmationDialogService, DialogHostConfirmationService>();

        // Register KISS transport (TCP is always available; BLE/SPP come from platform)
        services.AddSingleton<IKissStreamConnector, TcpKissStreamConnector>();
        services.AddSingleton<IKissStreamFactory, KissStreamFactory>();

        // Register port service
        services.AddSingleton<IPortService, PortService>();

        // Register digipeater service
        services.AddSingleton<IDigipeaterService, DigipeaterService>();

        // Register APRS settings resolver
        services.AddSingleton<IAprsPortSettingsResolver, AprsPortSettingsResolver>();

        // Register beacon service
        services.AddSingleton<IBeaconService, BeaconService>();

        // Register messaging
        services.AddSingleton<IMessageService, MessageService>();
        services.AddSingleton<IMessageTemplateService, MessageTemplateService>();
        services.AddSingleton<IPacketStorageService, PacketStorageService>();
        services.AddSingleton<IPacketQueryService, PacketQueryService>();
        services.AddSingleton<IAprsSymbolBitmapProvider, AprsSymbolBitmapProvider>();
        services.AddSingleton<AprsSymbolMapConverter>();

        // Register map layer services
        services.AddSingleton<IReceivedBeaconsMapLayerService, ReceivedBeaconsMapLayerService>();
        services.AddSingleton<IUserLocationLayerService, UserLocationLayerService>();

        // Register location and transmission services
        services.AddSingleton<ILocationTrackingService, LocationTrackingService>();
        services.AddSingleton<IBeaconTransmissionService, BeaconTransmissionService>();

        // Register factories
        services.AddSingleton<IPacketFactory, PacketFactory>();
        services.AddSingleton<IAddEditPortViewModelFactory, AddEditPortViewModelFactory>();
        services.AddSingleton<IPacketDetailsViewModelFactory, PacketDetailsViewModelFactory>();
        services.AddSingleton<IConversationViewModelFactory, ConversationViewModelFactory>();
        services.AddSingleton<IMessageTemplateDialogViewModelFactory, MessageTemplateDialogViewModelFactory>();
        services.AddSingleton<IMessageTemplateEditViewModelFactory, MessageTemplateEditViewModelFactory>();

        // Register ViewModels
        services.AddSingleton<MainViewModel>(); // Application-wide navigation state
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
        services.AddTransient<MessageTemplateDialogViewModel>(); // Dialog content, fresh each time a template is picked
        services.AddSingleton<MessageTemplatesViewModel>(); // Overlay page state
        services.AddTransient<MessageTemplateEditViewModel>(); // Dialog content, fresh each time a template is added/edited
        services.AddTransient<PacketDetailsViewModel>(); // Overlay sub-page, fresh each time

        // Allow overriding core services for testing or platform-specific
        // implementations
        overrideCoreServices(services);

        // Build the final service provider
        _serviceProvider = services.BuildServiceProvider();
        return _serviceProvider;
    }
}
