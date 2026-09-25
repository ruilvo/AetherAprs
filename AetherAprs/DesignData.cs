// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Data;
using AetherAprs.Factories;
using AetherAprs.Imaging;
using AetherAprs.Models;
using AetherAprs.Models.Aprs;
using AetherAprs.Services;
using AetherAprs.Services.Bluetooth;
using AetherAprs.Transports.Kiss;
using AetherAprs.ViewModels;
using AetherAprs.ViewModels.Components;
using AetherAprs.ViewModels.Pages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
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
        services.AddSingleton<IAppDataDirProviderService, AppDataDirProviderService>();
        services.AddSingleton<IUiCultureProvider, OsUiCultureProvider>();
        services.AddDbContextFactory<AppDbContext>((sp, options) =>
        {
            var directory = sp.GetRequiredService<IAppDataDirProviderService>().GetAppDataDirectory();
            var path = Path.Combine(directory, AppDbContext.DatabaseFileName);
            options.UseSqlite($"Data Source={path}");
        });
        services.AddSingleton<AppSavedDataInitializer>();
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<ILocationService, DesignTimeLocationService>();
        services.AddSingleton<IConfigurationService, ConfigurationService>();
        services.AddSingleton<IKissStreamConnector, TcpKissStreamConnector>();
        services.AddSingleton<IKissStreamConnector, UnsupportedBluetoothClassicKissStreamConnector>();
        services.AddSingleton<IKissStreamConnector, UnsupportedBluetoothLeKissStreamConnector>();
        services.AddSingleton<IKissStreamFactory, KissStreamFactory>();
        services.AddSingleton<IBluetoothLeScanner, UnsupportedBluetoothLeScanner>();
        services.AddSingleton<IBluetoothClassicDeviceProvider, UnsupportedBluetoothClassicDeviceProvider>();
        services.AddSingleton<IPortService, PortService>();
        services.AddSingleton<IDigipeaterService, DigipeaterService>();
        services.AddSingleton<IAprsPortSettingsResolver, AprsPortSettingsResolver>();
        services.AddSingleton<IBeaconService, BeaconService>();
        services.AddSingleton<IMessageService, MessageService>();
        services.AddSingleton<IPacketStorageService, PacketStorageService>();
        services.AddSingleton<IPacketQueryService, PacketQueryService>();
        services.AddSingleton<IAprsSymbolBitmapProvider, AprsSymbolBitmapProvider>();
        services.AddSingleton<AprsSymbolMapConverter>();
        services.AddSingleton<ReceivedBeaconsViewModel>();
        services.AddSingleton<IForegroundService, NoOpForegroundService>();

        // Register factories
        services.AddSingleton<IAddEditPortViewModelFactory, AddEditPortViewModelFactory>();
        services.AddSingleton<IPacketDetailsViewModelFactory, PacketDetailsViewModelFactory>();
        services.AddSingleton<IConversationViewModelFactory, ConversationViewModelFactory>();

        // Register logging
        services.AddLogging(builder =>
        {
            builder.AddDebug();
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Debug);
        });

        // Register view models
        services.AddSingleton<MainViewModel>();
        services.AddTransient<LocationTrackingViewModel>();
        services.AddTransient<BeaconTransmissionViewModel>();
        services.AddTransient<MapViewModel>();
        services.AddTransient<AprsSymbolPickerViewModel>();
        services.AddSingleton<HomeViewModel>();
        services.AddSingleton<MessagesViewModel>();
        services.AddSingleton<PacketsViewModel>();
        services.AddSingleton<PortsViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddTransient<DynamicBeaconingViewModel>();
        services.AddTransient<ConversationViewModel>();
        services.AddTransient<PacketDetailsViewModel>();

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
                Latitude = 38.7223,
                Longitude = -9.1393,
                Altitude = 100,
                Accuracy = 10,
                Timestamp = DateTimeOffset.Now
            });
        }
    }

    public static MainViewModel MainViewModel => _serviceProvider.GetRequiredService<MainViewModel>();

    public static HomeViewModel HomeViewModel => _serviceProvider.GetRequiredService<HomeViewModel>();

    public static MessagesViewModel MessagesViewModel => _serviceProvider.GetRequiredService<MessagesViewModel>();

    public static PacketsViewModel PacketsViewModel => _serviceProvider.GetRequiredService<PacketsViewModel>();

    public static SettingsViewModel SettingsViewModel => _serviceProvider.GetRequiredService<SettingsViewModel>();

    public static PortsViewModel PortsViewModel => _serviceProvider.GetRequiredService<PortsViewModel>();

    public static DynamicBeaconingViewModel DynamicBeaconingViewModel =>
        _serviceProvider.GetRequiredService<DynamicBeaconingViewModel>();

    public static ConversationViewModel ConversationViewModel =>
        _serviceProvider.GetRequiredService<ConversationViewModel>();

    public static PacketDetailsViewModel PacketDetailsViewModel =>
        _serviceProvider.GetRequiredService<PacketDetailsViewModel>();

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
                _serviceProvider.GetRequiredService<IBluetoothClassicDeviceProvider>());
            vm.Initialize(callsign, 1);
            return vm;
        }
    }
}
