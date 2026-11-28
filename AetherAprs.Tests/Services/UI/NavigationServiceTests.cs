// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Configuration.Settings;
using AetherAprs.Factories.Packets;
using AetherAprs.Factories.ViewModels;
using AetherAprs.Imaging;
using AetherAprs.Models;
using AetherAprs.Services.Beaconing;
using AetherAprs.Services.Configuration;
using AetherAprs.Services.Location;
using AetherAprs.Services.Messaging;
using AetherAprs.Services.Objects;
using AetherAprs.Services.Packets;
using AetherAprs.Services.Platform;
using AetherAprs.Services.Ports;
using AetherAprs.Services.Transmission;
using AetherAprs.Services.UI;
using AetherAprs.ViewModels;
using AetherAprs.ViewModels.Components;
using AetherAprs.ViewModels.Pages;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using NSubstitute;

using Xunit;

namespace AetherAprs.Tests.Services.UI;

public sealed class NavigationServiceTests
{
    [Fact]
    public void NavigateToInstance_GoBack_RestoresParentInstance()
    {
        INavigationService navigation = CreateNavigation(out _);
        navigation.NavigateTo<PortsViewModel>();
        ViewModelBase? ports = navigation.CurrentViewModel;

        OverlayViewModel overlay = new();
        navigation.NavigateTo(overlay);

        Assert.Same(overlay, navigation.CurrentViewModel);
        Assert.True(navigation.CanGoBack);

        navigation.GoBack();
        Assert.Same(ports, navigation.CurrentViewModel);
    }

    [Fact]
    public void NavigateToRootTab_ClearsStack()
    {
        INavigationService navigation = CreateNavigation(out ServiceProvider? provider);
        navigation.NavigateTo<PortsViewModel>();
        navigation.NavigateTo(new OverlayViewModel());
        navigation.NavigateTo<HomeViewModel>();

        Assert.IsType<HomeViewModel>(navigation.CurrentViewModel);

        bool exited = false;
        navigation.RequestAppExit += (_, _) => exited = true;
        navigation.GoBack();
        Assert.True(exited);
        Assert.Same(provider.GetRequiredService<HomeViewModel>(), navigation.CurrentViewModel);
    }

    [Fact]
    public void NavigateToDynamicBeaconing_GoBack_ReturnsToSettings()
    {
        INavigationService navigation = CreateNavigation(out _);
        navigation.NavigateTo<SettingsViewModel>();
        ViewModelBase? settings = navigation.CurrentViewModel;

        navigation.NavigateTo<DynamicBeaconingViewModel>();
        Assert.IsType<DynamicBeaconingViewModel>(navigation.CurrentViewModel);

        navigation.GoBack();
        Assert.Same(settings, navigation.CurrentViewModel);
    }

    private static INavigationService CreateNavigation(out ServiceProvider provider)
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton<INavigationService, NavigationService>();

        IPortService portService = Substitute.For<IPortService>();
        portService.Ports.Returns([]);
        services.AddSingleton(portService);
        IConfigurationService configuration = Substitute.For<IConfigurationService>();
        configuration.Current.Returns(new AppSettings());
        configuration.Current.Returns(_ => new AppSettings());
        services.AddSingleton(configuration);
        IBeaconService beaconService = Substitute.For<IBeaconService>();
        BeaconConfig walk = AetherAprs.Models.BeaconConfig.CreateWalkPreset();
        beaconService.AllConfigurations.Returns(
        [
            walk,
            AetherAprs.Models.BeaconConfig.CreateDrivePreset(),
            AetherAprs.Models.BeaconConfig.CreateCustomPreset()
        ]);
        beaconService.CurrentConfiguration.Returns(walk);
        services.AddSingleton(beaconService);
        services.AddSingleton(Substitute.For<IMessageService>());
        services.AddSingleton(Substitute.For<ILocationService>());
        services.AddSingleton(Substitute.For<IAprsPortSettingsResolver>());
        services.AddSingleton(Substitute.For<IPacketFactory>());
        services.AddSingleton(Substitute.For<IAprsSymbolBitmapProvider>());
        services.AddSingleton(Substitute.For<ILogger<HomeViewModel>>());
        services.AddSingleton(Substitute.For<ILogger<PacketsViewModel>>());

        services.AddSingleton<AetherAprs.Services.UI.IUiDispatcher>(AetherAprs.Tests.Helpers.SynchronousUiDispatcher.Instance);
        services.AddSingleton<AetherAprs.Services.UI.IConfirmationDialogService, AetherAprs.Tests.Helpers.StubConfirmationDialogService>();
        // Register IPacketQueryService and IPacketStorageService - required by ViewModels
        services.AddSingleton(Substitute.For<IPacketQueryService>());
        services.AddSingleton(Substitute.For<IPacketStorageService>());
        services.AddSingleton<IPermissionService>(Substitute.For<IPermissionService>());
        services.AddSingleton<IPermissionAttentionReporter, PermissionAttentionService>();

        // Register services that replaced ViewModels
        services.AddSingleton(Substitute.For<ILocationTrackingService>());
        services.AddSingleton(Substitute.For<ILocationCoordinator>());
        services.AddSingleton(Substitute.For<IBeaconTransmissionService>());
        services.AddSingleton(Substitute.For<IBeaconTransmitQueue>());
        services.AddSingleton(Substitute.For<IReceivedBeaconsMapLayerService>());
        services.AddSingleton(Substitute.For<IUserLocationLayerService>());
        services.AddSingleton(Substitute.For<IMapTileSourceService>());

        // Register IAddEditPortViewModelFactory - required by PortsViewModel
        services.AddSingleton(Substitute.For<IAddEditPortViewModelFactory>());

        // Register IPacketDetailsViewModelFactory - required by HomeViewModel
        services.AddSingleton(Substitute.For<IPacketDetailsViewModelFactory>());

        // Register IObjectDetailsViewModelFactory - required by HomeViewModel and PacketsViewModel
        services.AddSingleton(Substitute.For<IObjectDetailsViewModelFactory>());

        // Register IConversationViewModelFactory - required by MessagesViewModel
        services.AddSingleton(Substitute.For<IConversationViewModelFactory>());

        // Register IMessageTemplateService and edit factory - required by MessageTemplatesViewModel
        services.AddSingleton(Substitute.For<IMessageTemplateService>());
        services.AddSingleton(Substitute.For<IMessageTemplateEditViewModelFactory>());
        services.AddSingleton<MessageTemplatesViewModel>();

        // Register object/bulletin services and factory - required by ObjectsViewModel/BulletinsViewModel
        services.AddSingleton(Substitute.For<IObjectService>());
        services.AddSingleton(Substitute.For<IBulletinService>());
        services.AddSingleton(Substitute.For<IObjectEditorViewModelFactory>());

        services.AddSingleton<MapViewModel>();
        services.AddTransient<AprsSymbolPickerViewModel>(); // Required by SettingsViewModel
        services.AddSingleton<HomeViewModel>();
        services.AddSingleton<MessagesViewModel>();
        services.AddSingleton<PacketsViewModel>();
        services.AddSingleton<PortsViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddTransient<DynamicBeaconingViewModel>();
        services.AddSingleton<ObjectsViewModel>();
        services.AddTransient<ObjectEditorViewModel>();
        services.AddSingleton<BulletinsViewModel>();
        services.AddSingleton<SearchViewModel>();

        provider = services.BuildServiceProvider();
        return provider.GetRequiredService<INavigationService>();
    }

    public sealed class OverlayViewModel : ViewModelBase;
}
