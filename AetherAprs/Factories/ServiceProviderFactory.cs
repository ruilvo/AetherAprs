// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Data;
using AetherAprs.Data.Beaconing;
using AetherAprs.Data.Maintenance;
using AetherAprs.Data.MapTiles;
using AetherAprs.Data.Messaging;
using AetherAprs.Data.Packets;
using AetherAprs.Data.Ports;
using AetherAprs.Data.Transmission;
using AetherAprs.Factories.Packets;
using AetherAprs.Factories.ViewModels;
using AetherAprs.Imaging;
using AetherAprs.Services.Beaconing;
using AetherAprs.Services.Bluetooth;
using AetherAprs.Services.Configuration;
using AetherAprs.Services.Lifecycle;
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
    /// <summary>
    /// Builds the application container.
    /// </summary>
    /// <param name="appDataDirProvider">
    /// The platform bootstrap that already knows where application data lives.
    /// It is supplied by the platform instead of being resolved from a throwaway
    /// container, because the configuration file and the database path are needed
    /// before the container exists.
    /// </param>
    /// <param name="registerPlatformServices">Platform services to add.</param>
    /// <param name="overrideCoreServices">
    /// Platform replacements for core registrations. Replacements are explicit:
    /// <see cref="CoreServiceOverrides.Replace{TService, TImplementation}"/> fails
    /// when the core does not register the service.
    /// </param>
    /// <returns>The validated application service provider.</returns>
    public static IServiceProvider CreateServiceProvider(
        IAppDataDirProviderService appDataDirProvider,
        Action<IServiceCollection> registerPlatformServices,
        Action<CoreServiceOverrides> overrideCoreServices)
    {
        ArgumentNullException.ThrowIfNull(appDataDirProvider);
        ArgumentNullException.ThrowIfNull(registerPlatformServices);
        ArgumentNullException.ThrowIfNull(overrideCoreServices);

        ServiceCollection services = new();

        services.AddSingleton(appDataDirProvider);

        // Register safe core defaults for platform services so that any host can
        // start without a platform-specific implementation. Platform overrides are
        // applied afterwards, in OverrideCoreServices.
        //
        // Hosts without a platform secret store must fail closed rather than
        // silently persisting credentials in plaintext. Android replaces this
        // registration with its Keystore-backed implementation below.
        services.AddSingleton<ISecretProtector, NullSecretProtector>();
        services.AddSingleton<ILocationService, NoOpLocationService>();
        services.AddSingleton<IPermissionService, NoOpPermissionService>();
        services.AddSingleton<IPermissionAttentionReporter, PermissionAttentionService>();
        services.AddSingleton<IPortPermissionPrompter, PortPermissionPrompter>();
        services.AddSingleton<IForegroundService, NoOpForegroundService>();
        services.AddSingleton<ICompassService, NoOpCompassService>();
        services.AddSingleton<IKeepAliveService, KeepAliveService>();
        services.AddSingleton<INotificationService, NoOpNotificationService>();
        services.AddSingleton<IAudioStreamService, NoOpAudioStreamService>();
        services.AddSingleton<IAudioPlaybackService, NoOpAudioPlaybackService>();
        services.AddSingleton<IPttService, NoOpPttService>();
        // One route monitor and one transmit gate per process: the platform
        // observation feeds the monitor and every transmit path authorizes the same
        // generation, so a route change is seen by the keying and playback paths alike.
        services.AddSingleton<AudioRouteMonitor>();
        services.AddSingleton<AudioTransmitGate>();
        services.AddSingleton<IBluetoothLeScanner, UnsupportedBluetoothLeScanner>();
        services.AddSingleton<IBluetoothClassicDeviceProvider, UnsupportedBluetoothClassicDeviceProvider>();
        services.AddSingleton<IUiCultureProvider, OsUiCultureProvider>();

        // Register platform-specific services first
        registerPlatformServices(services);

        // The bootstrap already knows the directories. The private directory hosts
        // the database (credentials); the user-accessible directory hosts
        // configuration and caches.
        string appDataDirectory = appDataDirProvider.GetAppDataDirectory();
        string privateAppDataDirectory = appDataDirProvider.GetPrivateAppDataDirectory();

        // Build configuration from files in app data directory
        IConfigurationRoot configuration = new ConfigurationBuilder()
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
            string path = Path.Combine(privateAppDataDirectory, AppDbContext.DatabaseFileName);
            options.UseSqlite($"Data Source={path}");
        });
        services.AddSingleton<AppSavedDataInitializer>();
        services.AddSqliteRuntime();
        services.AddSingleton(TimeProvider.System);

        // Durable configuration stores. Feature services depend on these
        // interfaces, never on a DbContext or the runtime writer directly.
        services.AddSingleton<IPortRegistry, PortRegistry>();
        services.AddSingleton<IBeaconStateStore, BeaconStateStore>();
        services.AddSingleton<OutboxMessageStore>();
        services.AddSingleton<IMessageCommands>(provider => provider.GetRequiredService<OutboxMessageStore>());
        services.AddSingleton<IMessageQueries>(provider => provider.GetRequiredService<OutboxMessageStore>());

        // Register configuration service
        services.AddSingleton<IConfigurationService, ConfigurationService>();
        services.AddSingleton<ISessionStateStore, SessionStateStore>();
        services.AddSingleton<DatabaseTileCache>();
        services.AddSingleton<IDiagnosticEventSink, BoundedDiagnosticEventStore>();
        services.AddSingleton<AndroidLifecycleState>();
        services.AddSingleton<RedactedSupportReportService>();
        services.AddSingleton<ISupportReportBuilder>(provider =>
            provider.GetRequiredService<RedactedSupportReportService>());
        services.AddSingleton<PortService>();
        services.AddSingleton<IPortService>(services => services.GetRequiredService<PortService>());
        services.AddSingleton<IPortServiceTelemetry>(services => services.GetRequiredService<PortService>());
        services.AddSingleton<IPortSessionRuntime>(services => services.GetRequiredService<PortService>());
        services.AddSingleton<ISessionPortController, PortServiceSessionController>();
        services.AddSingleton<SessionCoordinator>();
        services.AddSingleton<ISessionStatusSource>(
            services => services.GetRequiredService<SessionCoordinator>());
        services.AddSingleton<SessionCoordinatorTelemetryBridge>();

        // One stop path for the in-app exit and the notification action: disarm
        // the session, stop ports and location, and release every keep-alive reason.
        services.AddSingleton<IApplicationSessionControl, ApplicationSessionControl>();

        // Application lifecycle: one ordered startup sequence per process and one
        // deferred retention worker. The steps are declared in StartupStepFactory.
        services.AddSingleton<SessionStartup>();
        services.AddSingleton<IDatabaseMaintenanceWorker, DatabaseMaintenanceWorker>();
        services.AddSingleton<IFreeSpaceProvider, DriveFreeSpaceProvider>();
        services.AddSingleton<IStorageHealthMonitor>(provider => new StorageHealthMonitor(
            provider.GetRequiredService<IAppDataDirProviderService>(),
            provider.GetRequiredService<IFreeSpaceProvider>(),
            provider.GetRequiredService<ILogger<StorageHealthMonitor>>(),
            provider.GetRequiredService<IDiagnosticEventSink>(),
            provider.GetRequiredService<TimeProvider>()));
        services.AddSingleton<ILocalDataMaintenanceService, LocalDataMaintenanceService>();
        services.AddSingleton<IApplicationStartup>(provider => new ApplicationStartup(
            StartupStepFactory.Create(provider),
            provider.GetRequiredService<ILogger<ApplicationStartup>>(),
            provider.GetRequiredService<IDiagnosticEventSink>()));

        // Register navigation service
        services.AddSingleton<INavigationService, NavigationService>();

        // Presentation-owned state the messaging service reads to suppress notifications
        services.AddSingleton<IActiveConversationTracker, ActiveConversationTracker>();

        // Register keyboard insets service
        services.AddSingleton<IKeyboardInsetsService, KeyboardInsetsService>();

        // Register UI thread dispatcher (marshals work onto the Avalonia UI thread)
        services.AddSingleton<IUiDispatcher>(AvaloniaUiDispatcher.Instance);
        services.AddSingleton<IThemeService>(AvaloniaThemeService.Instance);

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

        // One transmit policy for every outbound producer: the façade and the
        // forwarding relay share it, so a packet reaches a modem one way only.
        services.AddSingleton<IPortTransmitPolicy, PortTransmitPolicy>();

        // Register the inbound queue and the forwarding queue. Each owns its own
        // bounded channel and consumer; feature code never queues packets itself.
        services.AddSingleton<IPacketIngestPipeline, PacketIngestPipeline>();

        // One bounded forwarding history is shared by the policy and the relay so a
        // route question has a single answer and neither type grows without bound.
        services.AddSingleton<DigipeatDecisionLog>();
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
        services.AddSingleton<ILocationCoordinator, LocationCoordinator>();
        services.AddSingleton<IBeaconTransmissionService, BeaconTransmissionService>();
        services.AddSingleton<IBeaconTransmitQueue, BeaconOutboxStore>();

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
        services.AddTransient<SupportViewModel>(); // Overlay sub-page, fresh each time
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

        // Platform replacements are explicit and applied after every core
        // registration exists, so nothing depends on registration order.
        overrideCoreServices(new CoreServiceOverrides(services));

        // Validate the whole graph on every startup: a missing or unreachable
        // registration must fail here with the offending type instead of surfacing
        // later as a navigation or packet-handling failure. Scope validation also
        // catches a singleton that captures a scoped service.
        ServiceProviderOptions providerOptions = new()
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        };

        // Build the final service provider
        return services.BuildServiceProvider(providerOptions);
    }
}
