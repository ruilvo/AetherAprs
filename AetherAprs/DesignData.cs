// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Data;
using AetherAprs.Data.Beaconing;
using AetherAprs.Data.Packets;
using AetherAprs.Data.Ports;
using AetherAprs.Configuration.Settings;
using AetherAprs.Factories.Packets;
using AetherAprs.Factories.ViewModels;
using AetherAprs.Imaging;
using AetherAprs.Models;
using AetherAprs.Models.Aprs;
using AetherAprs.Services.Beaconing;
using AetherAprs.Services.Bluetooth;
using AetherAprs.Services.Configuration;
using AetherAprs.Services.Location;
using AetherAprs.Services.Messaging;
using AetherAprs.Services.Objects;
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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AetherAprs;

/// <summary>
/// Provides design-time data instances for use in XAML previews.
/// </summary>
public static class DesignData
{
    private static readonly ServiceProvider _serviceProvider = CreateDesignTimeServiceProvider();
    private static ServiceProvider CreateDesignTimeServiceProvider()
    {
        var services = new ServiceCollection();

        // Register services required by ViewModels
        services.AddSingleton<IAppDataDirProviderService, DesignTimeAppDataDirProviderService>();
        services.AddSingleton<IUiCultureProvider, OsUiCultureProvider>();
        services.AddDbContextFactory<AppDbContext>((sp, options) =>
        {
            var directory = sp.GetRequiredService<IAppDataDirProviderService>().GetAppDataDirectory();
            var path = Path.Combine(directory, AppDbContext.DatabaseFileName);
            options.UseSqlite($"Data Source={path}");
        });
        services.AddSingleton<AppSavedDataInitializer>();
        services.AddSqliteRuntime();

        // Design-time durable stores. The designer never has a platform secret
        // protector, so the cleartext fallback is the honest choice here.
        services.AddSingleton<ISecretProtector, PlaintextSecretProtector>();
        services.AddSingleton<IPortRegistry, PortRegistry>();
        services.AddSingleton<IBeaconStateStore, BeaconStateStore>();
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<IKeyboardInsetsService, KeyboardInsetsService>();

        // Design-time UI dispatcher runs actions synchronously (no Avalonia UI thread in designer)
        services.AddSingleton<IUiDispatcher>(AvaloniaUiDispatcher.Instance);
        services.AddSingleton<IConfirmationDialogService, DialogHostConfirmationService>();
        services.AddSingleton<ILocationService, DesignTimeLocationService>();
        services.AddSingleton<ICompassService, DesignTimeCompassService>();
        services.AddSingleton<IConfigurationService, ConfigurationService>();
        services.AddSingleton<IKissStreamConnector, TcpKissStreamConnector>();
        services.AddSingleton<IKissStreamConnector, UnsupportedBluetoothClassicKissStreamConnector>();
        services.AddSingleton<IKissStreamConnector, UnsupportedBluetoothLeKissStreamConnector>();
        services.AddSingleton<IKissStreamFactory, KissStreamFactory>();

        // Design-time audio and PTT are no-ops: the designer must never open a
        // microphone or key a transmitter.
        services.AddSingleton<IAudioStreamService, NoOpAudioStreamService>();
        services.AddSingleton<IAudioPlaybackService, NoOpAudioPlaybackService>();
        services.AddSingleton<IPttService, NoOpPttService>();
        services.AddSingleton<IModemFactory, ModemFactory>();
        services.AddSingleton<IPortSessionManager, PortSessionManager>();
        services.AddSingleton<IPortConfigurationService, PortConfigurationService>();
        services.AddSingleton<IPortConfigurationSource>(services =>
            services.GetRequiredService<IPortConfigurationService>());
        services.AddSingleton<IPacketIngestPipeline, PacketIngestPipeline>();
        services.AddSingleton<IDigipeatRouter, DigipeatRouter>();

        services.AddSingleton<IBluetoothLeScanner, UnsupportedBluetoothLeScanner>();
        services.AddSingleton<IBluetoothClassicDeviceProvider, UnsupportedBluetoothClassicDeviceProvider>();
        services.AddSingleton<IPortService, PortService>();
        services.AddSingleton<IDigipeaterService, DigipeaterService>();
        services.AddSingleton<IAprsPortSettingsResolver, AprsPortSettingsResolver>();
        services.AddSingleton<IBeaconService, BeaconService>();
        services.AddSingleton<IMessageService, MessageService>();
        services.AddSingleton<IMessageTemplateService, DesignTimeMessageTemplateService>();
        services.AddSingleton<IBulletinService, BulletinService>();
        services.AddSingleton<IObjectService, ObjectService>();
        services.AddSingleton<IPacketStorageService, PacketStorageService>();
        services.AddSingleton<IPacketQueryService, PacketQueryService>();
        services.AddSingleton<IAprsSymbolBitmapProvider, AprsSymbolBitmapProvider>();
        services.AddSingleton<AprsSymbolMapConverter>();
        services.AddSingleton<IForegroundService, DesignTimeForegroundService>();
        services.AddSingleton<IKeepAliveService, KeepAliveService>();
        services.AddSingleton<INotificationService, NoOpNotificationService>();

        // Register map layer services
        services.AddSingleton<IReceivedBeaconsMapLayerService, ReceivedBeaconsMapLayerService>();
        services.AddSingleton<IUserLocationLayerService, UserLocationLayerService>();
        services.AddSingleton<IMapTileSourceService, MapTileSourceService>();
        services.AddSingleton<IOfflineTilePackageDownloader, OfflineTilePackageDownloader>();

        // Register location and transmission services
        services.AddSingleton<ILocationTrackingService, LocationTrackingService>();
        services.AddSingleton<IBeaconTransmissionService, BeaconTransmissionService>();

        // Register factories
        services.AddSingleton<IPacketFactory, PacketFactory>();
        services.AddSingleton<IAddEditPortViewModelFactory, AddEditPortViewModelFactory>();
        services.AddSingleton<IPacketDetailsViewModelFactory, PacketDetailsViewModelFactory>();
        services.AddSingleton<IObjectDetailsViewModelFactory, ObjectDetailsViewModelFactory>();
        services.AddSingleton<IConversationViewModelFactory, ConversationViewModelFactory>();
        services.AddSingleton<IMessageTemplateDialogViewModelFactory, MessageTemplateDialogViewModelFactory>();
        services.AddSingleton<IMessageTemplateEditViewModelFactory, MessageTemplateEditViewModelFactory>();
        services.AddSingleton<IObjectEditorViewModelFactory, ObjectEditorViewModelFactory>();

        // Register logging
        services.AddLogging(builder =>
        {
            builder.AddDebug();
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Debug);
        });

        // Register view models
        services.AddSingleton<MainViewModel>();
        services.AddTransient<MapViewModel>();
        services.AddTransient<AprsSymbolPickerViewModel>();
        services.AddTransient<AprsIsFilterBuilderViewModel>();
        services.AddSingleton<HomeViewModel>();
        services.AddSingleton<MessagesViewModel>();
        services.AddSingleton<PacketsViewModel>();
        services.AddSingleton<PortsViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddTransient<DynamicBeaconingViewModel>();
        services.AddTransient<DownloadOfflineMapViewModel>();
        services.AddTransient<ConversationViewModel>();
        services.AddTransient<MessageTemplateDialogViewModel>();
        services.AddSingleton<MessageTemplatesViewModel>();
        services.AddTransient<MessageTemplateEditViewModel>();
        services.AddTransient<PacketDetailsViewModel>();
        services.AddTransient<ObjectDetailsViewModel>();
        services.AddSingleton<ObjectsViewModel>();
        services.AddTransient<ObjectEditorViewModel>();
        services.AddSingleton<BulletinsViewModel>();
        services.AddSingleton<SearchViewModel>();

        var provider = services.BuildServiceProvider();
        Localization.UiCulture.Apply(provider.GetRequiredService<IUiCultureProvider>().GetUiCulture());
        provider.GetRequiredService<AppSavedDataInitializer>().Initialize();
        return provider;
    }

    /// <summary>
    /// Design-time stub implementation of ILocationService.
    /// </summary>
    private class DesignTimeLocationService : ILocationService
    {
        public bool IsLocationAvailable() => true;

        public Task<bool> RequestLocationPermissionAsync() => Task.FromResult(true);

        public Task<LocationData> GetCurrentLocationAsync(CancellationToken cancellationToken = default)
        {
            // Return a fake location (Lisbon, Portugal coordinates as example)
            return Task.FromResult(new LocationData
            {
                Location = new Geo.Coordinate(38.7223, -9.1393),
                Altitude = 100,
                Accuracy = 10,
                Timestamp = DateTimeOffset.Now
            });
        }
    }

    /// <summary>
    /// Design-time stub implementation of ICompassService reporting a fixed
    /// heading so the radar needle renders in the designer.
    /// </summary>
    private sealed class DesignTimeCompassService : ICompassService
    {
        public bool IsAvailable => true;

        public double? HeadingDegrees => 45.0;

        public event EventHandler<double>? HeadingUpdated
        {
            add { }
            remove { }
        }

        public void StartListening()
        {
        }

        public void StopListening()
        {
        }

        public void Dispose()
        {
        }
    }

    /// <summary>
    /// Design-time stub implementation of IAppDataDirProviderService.
    /// </summary>
    private class DesignTimeAppDataDirProviderService : IAppDataDirProviderService
    {
        public string GetAppDataDirectory() => AppContext.BaseDirectory;

        public string GetPrivateAppDataDirectory() => AppContext.BaseDirectory;
    }

    /// <summary>
    /// Design-time stub implementation of IForegroundService. There is no
    /// runtime desktop implementation of IForegroundService.
    /// </summary>
    private class DesignTimeForegroundService : IForegroundService
    {
        public bool IsRunning => false;

        public Task StartAsync(ForegroundServiceType requiredTypes) => Task.CompletedTask;

        public Task StopAsync() => Task.CompletedTask;
    }

    /// <summary>
    /// Design-time stub implementation of IMessageTemplateService offering the
    /// seeded default template without touching configuration files.
    /// </summary>
    private sealed class DesignTimeMessageTemplateService : IMessageTemplateService
    {
        private static readonly IReadOnlyList<MessageTemplate> SeedTemplates =
        [
            new MessageTemplate
            {
                Name = "APRS2SOTA Spot",
                Destination = "SOTA",
                Body = "{summit} {freq} {mode} {comment}"
            }
        ];

        public IReadOnlyList<MessageTemplate> Templates => SeedTemplates;

        public MessageTemplate? GetByName(string name) =>
            name is not null
                ? SeedTemplates.FirstOrDefault(template =>
                    string.Equals(template.Name, name, StringComparison.OrdinalIgnoreCase))
                : null;

        public Task AddAsync(MessageTemplate messageTemplate, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task UpdateAsync(string originalName, MessageTemplate messageTemplate, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task DeleteAsync(string name, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public IReadOnlyList<string> GetPlaceholderNames(string body) => [];

        public string? Apply(MessageTemplate messageTemplate, IReadOnlyDictionary<string, string> values) =>
            messageTemplate.Body;

        public IReadOnlyList<MessageTemplate> GetTemplatesForPeer(Callsign peer) => SeedTemplates;
    }

    public static MainViewModel MainViewModel => _serviceProvider.GetRequiredService<MainViewModel>();

    public static HomeViewModel HomeViewModel => _serviceProvider.GetRequiredService<HomeViewModel>();

    public static MessagesViewModel MessagesViewModel => _serviceProvider.GetRequiredService<MessagesViewModel>();

    public static PacketsViewModel PacketsViewModel => _serviceProvider.GetRequiredService<PacketsViewModel>();

    public static SettingsViewModel SettingsViewModel => _serviceProvider.GetRequiredService<SettingsViewModel>();

    public static PortsViewModel PortsViewModel => _serviceProvider.GetRequiredService<PortsViewModel>();

    public static DynamicBeaconingViewModel DynamicBeaconingViewModel =>
        _serviceProvider.GetRequiredService<DynamicBeaconingViewModel>();

    public static DownloadOfflineMapViewModel DownloadOfflineMapViewModel =>
        _serviceProvider.GetRequiredService<DownloadOfflineMapViewModel>();

    public static ConversationViewModel ConversationViewModel =>
        _serviceProvider.GetRequiredService<ConversationViewModel>();

    public static PacketDetailsViewModel PacketDetailsViewModel =>
        _serviceProvider.GetRequiredService<PacketDetailsViewModel>();

    public static ObjectDetailsViewModel ObjectDetailsViewModel =>
        _serviceProvider.GetRequiredService<ObjectDetailsViewModel>();

    public static MessageTemplateDialogViewModel MessageTemplateDialogViewModel
    {
        get
        {
            var templateService = _serviceProvider.GetRequiredService<IMessageTemplateService>();
            var factory = _serviceProvider.GetRequiredService<IMessageTemplateDialogViewModelFactory>();
            return factory.Create(templateService.Templates, null);
        }
    }

    public static MessageTemplatesViewModel MessageTemplatesViewModel =>
        _serviceProvider.GetRequiredService<MessageTemplatesViewModel>();

    public static ObjectsViewModel ObjectsViewModel =>
        _serviceProvider.GetRequiredService<ObjectsViewModel>();

    public static ObjectEditorViewModel ObjectEditorViewModel =>
        _serviceProvider.GetRequiredService<ObjectEditorViewModel>();

    public static BulletinsViewModel BulletinsViewModel =>
        _serviceProvider.GetRequiredService<BulletinsViewModel>();

    public static SearchViewModel SearchViewModel =>
        _serviceProvider.GetRequiredService<SearchViewModel>();

    public static SymbolSelectorViewModel SymbolSelectorViewModel
    {
        get
        {
            var symbolBitmapProvider = _serviceProvider.GetRequiredService<IAprsSymbolBitmapProvider>();
            return new SymbolSelectorViewModel(symbolBitmapProvider, SymbolTable.Primary, SymbolCode.LeftSquareBracket);
        }
    }

    public static AddEditPortViewModel AddEditPortViewModel
    {
        get
        {
            var configService = _serviceProvider.GetRequiredService<IConfigurationService>();
            var callsign = configService.Settings.Aprs.Callsign;
            var vm = new AddEditPortViewModel(
                _serviceProvider.GetRequiredService<INavigationService>(),
                _serviceProvider.GetRequiredService<IPortService>(),
                _serviceProvider.GetRequiredService<IKissStreamFactory>(),
                _serviceProvider.GetRequiredService<IBluetoothLeScanner>(),
                _serviceProvider.GetRequiredService<IBluetoothClassicDeviceProvider>(),
                _serviceProvider.GetRequiredService<AprsIsFilterBuilderViewModel>());
            vm.Initialize(callsign, 1);
            return vm;
        }
    }
}
