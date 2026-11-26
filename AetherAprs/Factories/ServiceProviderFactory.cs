// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Data;
using AetherAprs.Data.Beaconing;
using AetherAprs.Data.Packets;
using AetherAprs.Data.Ports;
using AetherAprs.Factories.Packets;
using AetherAprs.Factories.ViewModels;
using AetherAprs.Imaging;
using AetherAprs.Services.Beaconing;
using AetherAprs.Services.Bluetooth;
using AetherAprs.Services.Configuration;
using AetherAprs.Services.Location;
using AetherAprs.Services.Maintenance;
using AetherAprs.Services.Messaging;
using AetherAprs.Services.Objects;
using AetherAprs.Services.Packets;
using AetherAprs.Services.Platform;
using AetherAprs.Services.Ports;
using AetherAprs.Services.Startup;
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
using AetherAprs.Services.Diagnostics;
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
        // Hosts without a platform secret store must fail closed rather than
        // silently persisting credentials in plaintext. Android replaces this
        // registration with its Keystore-backed implementation below.
        services.AddSingleton<ISecretProtector, NullSecretProtector>();
        services.AddSingleton<ILocationService, NoOpLocationService>();
        services.AddSingleton<IPermissionService, NoOpPermissionService>();
        services.AddSingleton<IForegroundService, NoOpForegroundService>();
        services.AddSingleton<ICompassService, NoOpCompassService>();
        services.AddSingleton<IKeepAliveService, KeepAliveService>();
        services.AddSingleton<INotificationService, NoOpNotificationService>();
        services.AddSingleton<IAudioStreamService, NoOpAudioStreamService>();
        services.AddSingleton<IAudioPlaybackService, NoOpAudioPlaybackService>();
        services.AddSingleton<IPttService, NoOpPttService>();
        services.AddSingleton<IBluetoothLeScanner, UnsupportedBluetoothLeScanner>();
        services.AddSingleton<IBluetoothClassicDeviceProvider, UnsupportedBluetoothClassicDeviceProvider>();
        services.AddSingleton<IUiCultureProvider, OsUiCultureProvider>();

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
        services.AddSqliteRuntime();

        // Durable configuration stores. Feature services depend on these
        // interfaces, never on a DbContext or the runtime writer directly.
        services.AddSingleton<IPortRegistry, PortRegistry>();
        services.AddSingleton<IBeaconStateStore, BeaconStateStore>();

        // Register configuration service
        services.AddSingleton<IConfigurationService, ConfigurationService>();
        services.AddSingleton<ISessionStateStore>(_ =>
            new FileSessionStateStore(Path.Combine(privateAppDataDirectory, "session-state.json")));
        services.AddSingleton<IDiagnosticEventSink, BoundedDiagnosticEventStore>();
        services.AddSingleton<AndroidLifecycleState>();
        services.AddSingleton<RedactedSupportReportService>();
        services.AddSingleton<PortService>();
        services.AddSingleton<IPortService>(services => services.GetRequiredService<PortService>());
        services.AddSingleton<IPortServiceTelemetry>(services => services.GetRequiredService<PortService>());
        services.AddSingleton<IPortSessionRuntime>(services => services.GetRequiredService<PortService>());
        services.AddSingleton<ISessionPortController, PortServiceSessionController>();
        services.AddSingleton<SessionCoordinator>();
        services.AddSingleton<SessionCoordinatorTelemetryBridge>();

        // Application lifecycle: one ordered startup sequence per process and one
        // deferred retention worker. The steps are declared in StartupStepFactory.
        services.AddSingleton<SessionStartup>();
        services.AddSingleton<IDatabaseMaintenanceWorker, DatabaseMaintenanceWorker>();
        services.AddSingleton<IApplicationStartup>(provider => new ApplicationStartup(
            StartupStepFactory.Create(provider),
            provider.GetRequiredService<ILogger<ApplicationStartup>>(),
            provider.GetRequiredService<IDiagnosticEventSink>()));

        // Register navigation service
        services.AddSingleton<INavigationService, NavigationService>();

        // Register keyboard insets service
        services.AddSingleton<IKeyboardInsetsService, KeyboardInsetsService>();

        // Register UI thread dispatcher (marshals work onto the Avalonia UI thread)
        services.AddSingleton<IUiDispatcher>(AvaloniaUiDispatcher.Instance);

        // Register confirmation dialog service (shows dialogs through the main DialogHost)
        services.AddSingleton<IConfirmationDialogService, DialogHostConfirmationService>();
        services.AddSingleton<ITransientFeedbackService, SnackbarTransientFeedbackService>();

        // Register KISS transport (TCP is always available; BLE/SPP come from platform)
        services.AddSingleton<IKissStreamConnector, TcpKissStreamConnector>();
        services.AddSingleton<IKissStreamFactory, KissStreamFactory>();

        // Register modem construction. PortService depends on the factory, never
        // on audio, PTT or transport connectors directly.
        services.AddSingleton<IModemFactory, ModemFactory>();

        // Register the single owner of running modem sessions. Configured ports,
        // session gates and send leases each have exactly one owner.
        services.AddSingleton<IPortSessionManager, PortSessionManager>();
        services.AddSingleton<IPortConfigurationService, PortConfigurationService>();
        services.AddSingleton<IPortConfigurationSource>(services =>
            services.GetRequiredService<IPortConfigurationService>());

        // Register the inbound queue and the forwarding queue. Each owns its own
        // bounded channel and consumer; feature code never queues packets itself.
        services.AddSingleton<IPacketIngestPipeline, PacketIngestPipeline>();
        services.AddSingleton<IDigipeatRouter, DigipeatRouter>();

        // Register port service

        // Register digipeater service
        services.AddSingleton<IDigipeaterService, DigipeaterService>();

        // Register APRS settings resolver
        services.AddSingleton<IAprsPortSettingsResolver, AprsPortSettingsResolver>();

        // Register beacon service
        services.AddSingleton<IBeaconService, BeaconService>();

        // Register messaging
        services.AddSingleton<IMessageService, MessageService>();
        services.AddSingleton<IMessageTemplateService, MessageTemplateService>();
        services.AddSingleton<IBulletinService, BulletinService>();
        services.AddSingleton<IObjectService, ObjectService>();
        services.AddSingleton<IPacketStorageService, PacketStorageService>();
        services.AddSingleton<IPacketQueryService, PacketQueryService>();
        services.AddSingleton<IAprsSymbolBitmapProvider, AprsSymbolBitmapProvider>();
        services.AddSingleton<AprsSymbolMapConverter>();

        // Register UI services
        services.AddSingleton<IPacketViewFilterService, PacketViewFilterService>();

        // Register map layer services
        services.AddSingleton<IReceivedBeaconsMapLayerService, ReceivedBeaconsMapLayerService>();
        services.AddSingleton<IUserLocationLayerService, UserLocationLayerService>();
        services.AddSingleton<IMapTileSourceService, MapTileSourceService>();
        services.AddSingleton<IOfflineTilePackageDownloader, OfflineTilePackageDownloader>();

        // Register location and transmission services
        services.AddSingleton<ILocationTrackingService, LocationTrackingService>();
        services.AddSingleton<IBeaconTransmissionService, BeaconTransmissionService>();
        services.AddSingleton<IBeaconTransmitQueue>(provider =>
            new FileBeaconTransmitQueue(
                Path.Combine(privateAppDataDirectory, "beacon-outbox.json"),
                provider.GetRequiredService<ILogger<FileBeaconTransmitQueue>>()));

        // Register factories
        services.AddSingleton<IPacketFactory, PacketFactory>();
        services.AddSingleton<IAddEditPortViewModelFactory, AddEditPortViewModelFactory>();
        services.AddSingleton<IPacketDetailsViewModelFactory, PacketDetailsViewModelFactory>();
        services.AddSingleton<IObjectDetailsViewModelFactory, ObjectDetailsViewModelFactory>();
        services.AddSingleton<IConversationViewModelFactory, ConversationViewModelFactory>();
        services.AddSingleton<IMessageTemplateDialogViewModelFactory, MessageTemplateDialogViewModelFactory>();
        services.AddSingleton<IMessageTemplateEditViewModelFactory, MessageTemplateEditViewModelFactory>();
        services.AddSingleton<IObjectEditorViewModelFactory, ObjectEditorViewModelFactory>();

        // Register ViewModels
        services.AddSingleton<MainViewModel>(); // Application-wide navigation state
        services.AddTransient<MapViewModel>(); // Sub-component, created per HomeViewModel
        services.AddTransient<AprsSymbolPickerViewModel>(); // Sub-component, created per SettingsViewModel
        services.AddTransient<AprsIsFilterBuilderViewModel>(); // Sub-component, created per AddEditPortViewModel
        services.AddSingleton<HomeViewModel>(); // Main page state
        services.AddSingleton<MessagesViewModel>(); // Main page state
        services.AddSingleton<PacketsViewModel>(); // Main page state
        services.AddSingleton<PortsViewModel>(); // Main page state
        services.AddSingleton<SettingsViewModel>(); // Main page state
        services.AddTransient<DynamicBeaconingViewModel>(); // Overlay sub-page, fresh each time
        services.AddTransient<DownloadOfflineMapViewModel>(); // Overlay sub-page, fresh each time
        services.AddTransient<AddEditPortViewModel>(); // Overlay sub-page, fresh each time
        services.AddTransient<ConversationViewModel>(); // Overlay sub-page, fresh each time
        services.AddTransient<MessageTemplateDialogViewModel>(); // Dialog content, fresh each time a template is picked
        services.AddSingleton<MessageTemplatesViewModel>(); // Overlay page state
        services.AddTransient<MessageTemplateEditViewModel>(); // Dialog content, fresh each time a template is added/edited
        services.AddTransient<PacketDetailsViewModel>(); // Overlay sub-page, fresh each time
        services.AddTransient<ObjectDetailsViewModel>(); // Overlay sub-page, fresh each time
        services.AddSingleton<ObjectsViewModel>(); // Overlay page state
        services.AddTransient<ObjectEditorViewModel>(); // Overlay sub-page, fresh each time (via factory)
        services.AddSingleton<BulletinsViewModel>(); // Overlay page state
        services.AddSingleton<SearchViewModel>(); // Overlay page state

        // Allow overriding core services for testing or platform-specific
        // implementations
        overrideCoreServices(services);

        // Build the final service provider
        _serviceProvider = services.BuildServiceProvider();
        return _serviceProvider;
    }
}
