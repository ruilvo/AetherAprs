// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using AetherAprs.Factories;
using AetherAprs.Imaging;
using AetherAprs.Services;
using AetherAprs.ViewModels;
using AetherAprs.ViewModels.Components;
using AetherAprs.ViewModels.Pages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace AetherAprs.Tests.Services;

public sealed class NavigationServiceTests
{
    [Fact]
    public void NavigateToInstance_GoBack_RestoresParentInstance()
    {
        var navigation = CreateNavigation(out _);
        navigation.NavigateTo<PortsViewModel>();
        var ports = navigation.CurrentViewModel;

        var overlay = new OverlayViewModel();
        navigation.NavigateTo(overlay);

        Assert.Same(overlay, navigation.CurrentViewModel);
        Assert.True(navigation.CanGoBack);

        navigation.GoBack();
        Assert.Same(ports, navigation.CurrentViewModel);
    }

    [Fact]
    public void NavigateToRootTab_ClearsStack()
    {
        var navigation = CreateNavigation(out var provider);
        navigation.NavigateTo<PortsViewModel>();
        navigation.NavigateTo(new OverlayViewModel());
        navigation.NavigateTo<HomeViewModel>();

        Assert.IsType<HomeViewModel>(navigation.CurrentViewModel);

        var exited = false;
        navigation.RequestAppExit += (_, _) => exited = true;
        navigation.GoBack();
        Assert.True(exited);
        Assert.Same(provider.GetRequiredService<HomeViewModel>(), navigation.CurrentViewModel);
    }

    [Fact]
    public void NavigateToDynamicBeaconing_GoBack_ReturnsToSettings()
    {
        var navigation = CreateNavigation(out _);
        navigation.NavigateTo<SettingsViewModel>();
        var settings = navigation.CurrentViewModel;

        navigation.NavigateTo<DynamicBeaconingViewModel>();
        Assert.IsType<DynamicBeaconingViewModel>(navigation.CurrentViewModel);

        navigation.GoBack();
        Assert.Same(settings, navigation.CurrentViewModel);
    }

    private static INavigationService CreateNavigation(out ServiceProvider provider)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<INavigationService, NavigationService>();

        var portService = Substitute.For<IPortService>();
        portService.Ports.Returns(Array.Empty<AetherAprs.Configuration.PortConfig>());
        services.AddSingleton(portService);
        var configuration = Substitute.For<IConfigurationService>();
        configuration.Settings.Returns(new AetherAprs.Configuration.AppSettings());
        services.AddSingleton(configuration);
        var beaconService = Substitute.For<IBeaconService>();
        var walk = AetherAprs.Models.BeaconConfig.CreateWalkPreset();
        beaconService.AllConfigurations.Returns(new[]
        {
            walk,
            AetherAprs.Models.BeaconConfig.CreateDrivePreset(),
            AetherAprs.Models.BeaconConfig.CreateCustomPreset()
        });
        beaconService.CurrentConfiguration.Returns(walk);
        services.AddSingleton(beaconService);
        services.AddSingleton(Substitute.For<IMessageService>());
        services.AddSingleton(Substitute.For<ILocationService>());
        services.AddSingleton(Substitute.For<IAprsPortSettingsResolver>());
        services.AddSingleton(Substitute.For<IAprsSymbolBitmapProvider>());
        services.AddSingleton(Substitute.For<ILogger<HomeViewModel>>());
        services.AddSingleton(Substitute.For<ILogger<LocationTrackingViewModel>>());
        services.AddSingleton(Substitute.For<ILogger<BeaconTransmissionViewModel>>());
        services.AddSingleton(Substitute.For<ILogger<ReceivedBeaconsViewModel>>());
        
        // Register IPacketCacheService - required by ReceivedBeaconsViewModel
        var packetCache = Substitute.For<IPacketCacheService>();
        packetCache.GetPositionPackets().Returns(new Dictionary<string, CachedPacket>());
        services.AddSingleton(packetCache);

        // Register IAddEditPortViewModelFactory - required by PortsViewModel
        services.AddSingleton(Substitute.For<IAddEditPortViewModelFactory>());

        services.AddSingleton<ReceivedBeaconsViewModel>();
        services.AddSingleton<LocationTrackingViewModel>();
        services.AddSingleton<BeaconTransmissionViewModel>();
        services.AddSingleton<MapViewModel>();
        services.AddTransient<AprsSymbolPickerViewModel>(); // Required by SettingsViewModel
        services.AddSingleton<HomeViewModel>();
        services.AddSingleton<MessagesViewModel>();
        services.AddSingleton<PortsViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddTransient<DynamicBeaconingViewModel>();

        provider = services.BuildServiceProvider();
        return provider.GetRequiredService<INavigationService>();
    }

    public sealed class OverlayViewModel : ViewModelBase;
}
