// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using AetherAprs.Configuration;
using AetherAprs.Configuration.Settings;
using AetherAprs.Data;
using AetherAprs.Factories.Packets;
using AetherAprs.Factories.ViewModels;
using AetherAprs.Imaging;
using AetherAprs.Models;
using AetherAprs.Models.Aprs;
using AetherAprs.Models.Aprs.Packets;
using AetherAprs.Models.Messaging;
using AetherAprs.Services.Beaconing;
using AetherAprs.Services.Configuration;
using AetherAprs.Services.Contracts;
using AetherAprs.Services.Location;
using AetherAprs.Services.Messaging;
using AetherAprs.Services.Objects;
using AetherAprs.Services.Packets;
using AetherAprs.Services.Platform;
using AetherAprs.Services.Ports;
using AetherAprs.Services.Transmission;
using AetherAprs.Services.UI;
using AetherAprs.Tests.Helpers;
using AetherAprs.ViewModels;
using Geo;
using Mapsui.Layers;
using AetherAprs.ViewModels.Components;
using AetherAprs.ViewModels.Pages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SkiaSharp;
using Xunit;

namespace AetherAprs.Tests.ViewModels;

public sealed class MainViewModelTests
{
    [Fact]
    public void Constructor_NavigatesToHome()
    {
        TestNavigationService navigation = new();
        MainViewModel fixture = CreateFixture(navigation);

        Assert.Equal(typeof(HomeViewModel), navigation.LastNavigatedType);
        Assert.Equal(0, fixture.SelectedTabIndex);
        Assert.False(fixture.IsOverlayVisible);
    }

    [Fact]
    public void SelectedTabIndex_WithoutOverlay_NavigatesToMappedTab()
    {
        TestNavigationService navigation = new();
        MainViewModel fixture = CreateFixture(navigation);

        fixture.SelectedTabIndex = 2;

        Assert.Equal(typeof(PacketsViewModel), navigation.LastNavigatedType);
    }

    [Fact]
    public void SelectedTabIndex_WithOverlay_DoesNotNavigate()
    {
        TestNavigationService navigation = new();
        MainViewModel fixture = CreateFixture(navigation);
        navigation.RaiseCurrentChanged(new OverlayStubViewModel());
        navigation.LastNavigatedType = null;

        fixture.SelectedTabIndex = 1;

        Assert.Null(navigation.LastNavigatedType);
        Assert.True(fixture.IsOverlayVisible);
    }

    [Fact]
    public void NavigationToTabType_ClearsOverlayAndSetsTabIndex()
    {
        TestNavigationService navigation = new();
        MainViewModel fixture = CreateFixture(navigation);
        navigation.RaiseCurrentChanged(new OverlayStubViewModel());

        navigation.RaiseCurrentChanged(fixture.SettingsViewModel);

        Assert.Null(fixture.OverlayViewModel);
        Assert.False(fixture.IsOverlayVisible);
        Assert.Equal(4, fixture.SelectedTabIndex);
    }

    [Fact]
    public void NavigationToNonTab_SetsOverlay()
    {
        TestNavigationService navigation = new();
        MainViewModel fixture = CreateFixture(navigation);
        OverlayStubViewModel overlay = new();

        navigation.RaiseCurrentChanged(overlay);

        Assert.Same(overlay, fixture.OverlayViewModel);
        Assert.True(fixture.IsOverlayVisible);
    }

    private static MainViewModel CreateFixture(TestNavigationService navigation)
    {
        EmptyPortService portService = new();
        TestConfigurationService configuration = new();
        FakeMessageService messageService = new();
        ServiceCollection services = new();
        services.AddSingleton<IMessageService>(messageService);
        services.AddSingleton<INavigationService>(navigation);
        services.AddLogging();
        services.AddSingleton(Substitute.For<IMessageTemplateService>());
        services.AddSingleton(Substitute.For<IMessageTemplateDialogViewModelFactory>());
        services.AddTransient<ConversationViewModel>();
        services.AddSingleton<IConversationViewModelFactory, ConversationViewModelFactory>();
        ServiceProvider provider = services.BuildServiceProvider();
        IConversationViewModelFactory conversationFactory = provider.GetRequiredService<IConversationViewModelFactory>();
        _ = Substitute.For<IDbContextFactory<AppDbContext>>();
        TestSymbolBitmapProvider symbolProvider = new();
        IPacketQueryService packetQuery = Substitute.For<IPacketQueryService>();
        IPacketStorageService packetStorage = Substitute.For<IPacketStorageService>();

        // Use services instead of ViewModels
        IReceivedBeaconsMapLayerService receivedBeaconsMapLayer = Substitute.For<IReceivedBeaconsMapLayerService>();
        ILocationTrackingService locationTracking = Substitute.For<ILocationTrackingService>();
        IBeaconTransmissionService beaconTransmission = Substitute.For<IBeaconTransmissionService>();
        IUserLocationLayerService userLocationLayer = Substitute.For<IUserLocationLayerService>();

        MapViewModel mapViewModel = new(userLocationLayer);

        IPacketDetailsViewModelFactory packetDetailsFactory = Substitute.For<IPacketDetailsViewModelFactory>();
        IObjectDetailsViewModelFactory objectDetailsFactory = Substitute.For<IObjectDetailsViewModelFactory>();
        INavigationService navigationService = Substitute.For<INavigationService>();

        IMapTileSourceService mapTileSource = Substitute.For<IMapTileSourceService>();
        mapTileSource.CreateBaseMapLayer().Returns(Substitute.For<ILayer>());

        HomeViewModel home = new(
            locationTracking,
            beaconTransmission,
            userLocationLayer,
            receivedBeaconsMapLayer,
            mapTileSource,
            portService,
            mapViewModel,
            packetDetailsFactory,
            objectDetailsFactory,
            navigationService,
            SynchronousUiDispatcher.Instance,
            new PermissionAttentionService(),
            Substitute.For<IPermissionService>(),
            NullLogger<HomeViewModel>.Instance);
        MessagesViewModel messages = new(messageService, navigation, conversationFactory,
            new BulletinsViewModel(
                Substitute.For<IBulletinService>(),
                navigation));

        IPacketViewFilterService filterService = Substitute.For<IPacketViewFilterService>();
        filterService.GetCutoffTime().Returns((DateTimeOffset?)null);

        PacketsViewModel packets = new(
            packetQuery,
            packetStorage,
            navigation,
            packetDetailsFactory,
            objectDetailsFactory,
            configuration,
            filterService,
            SynchronousUiDispatcher.Instance,
            NullLogger<PacketsViewModel>.Instance,
            new ObjectsViewModel(
                Substitute.For<IObjectService>(),
                navigation,
                Substitute.For<IObjectEditorViewModelFactory>(),
                Substitute.For<IConfirmationDialogService>()),
            new SearchViewModel(
                packetQuery,
                navigation,
                packetDetailsFactory,
                Substitute.For<IObjectDetailsViewModelFactory>()));
        IAddEditPortViewModelFactory factory = Substitute.For<IAddEditPortViewModelFactory>();
        PortsViewModel ports = new(portService, configuration, navigation, factory, new TestConfirmationDialogService(), NullLogger<PortsViewModel>.Instance, NullLoggerFactory.Instance);
        AprsSymbolPickerViewModel symbolPicker = new(symbolProvider);
        SettingsViewModel settings = new(
            configuration, navigation, symbolPicker, new MessageTemplatesViewModel(
                Substitute.For<IMessageTemplateService>(),
                Substitute.For<IConfirmationDialogService>(),
                navigation,
                Substitute.For<IMessageTemplateEditViewModelFactory>()));

        return new MainViewModel(navigation, Substitute.For<IKeyboardInsetsService>(), home, messages, packets, ports, settings);
    }

    public sealed class OverlayStubViewModel : ViewModelBase;

    private sealed class TestNavigationService : INavigationService
    {
        public ViewModelBase? CurrentViewModel { get; private set; }
        public Type? LastNavigatedType { get; set; }
        public bool CanGoBack => false;

        public event EventHandler<ViewModelBase?>? CurrentViewModelChanged;
#pragma warning disable CS0067
        public event EventHandler? RequestAppExit;
#pragma warning restore CS0067

        public void NavigateTo<TViewModel>() where TViewModel : ViewModelBase => LastNavigatedType = typeof(TViewModel);

        public void NavigateTo(ViewModelBase viewModel)
        {
            LastNavigatedType = viewModel.GetType();
            RaiseCurrentChanged(viewModel);
        }

        public void GoBack()
        {
        }

        public void RaiseCurrentChanged(ViewModelBase? viewModel)
        {
            CurrentViewModel = viewModel;
            CurrentViewModelChanged?.Invoke(this, viewModel);
        }
    }

    private sealed class EmptyPortService : IPortService
    {
        public IReadOnlyList<PortConfig> Ports { get; } = [];
#pragma warning disable CS0067
        public event EventHandler? PortsChanged;
        public event EventHandler<PortPacketReceivedEventArgs>? PacketReceived;
#pragma warning restore CS0067
        public Task AddPortAsync(PortConfig port) => Task.CompletedTask;
        public Task UpdatePortAsync(PortConfig port) => Task.CompletedTask;
        public Task RemovePortAsync(Guid id) => Task.CompletedTask;
        public Task SetPortEnabledAsync(Guid id, bool enabled) => Task.CompletedTask;
        public Task SetPortShowOnMapAsync(Guid id, bool showOnMap) => Task.CompletedTask;
        public Task SendPacketAsync(Guid id, AprsPacket packet) => Task.CompletedTask;
        public Task StopAllPortsAsync() => Task.CompletedTask;
        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class TestConfigurationService : IConfigurationService
    {
#pragma warning disable CS0067 // Event is never used in this test double
        public event EventHandler? SettingsChanged;
#pragma warning restore CS0067

        public AppSettings Current { get; set; } = new();

        public Task<AppSettings> UpdateAsync(
            Func<AppSettings, AppSettings> update,
            CancellationToken cancellationToken = default)
        {
            Current = update(Current);
            return Task.FromResult(Current);
        }
    }

    private sealed class FakeMessageService : IMessageService
    {
        private readonly List<ConversationThread> _conversations = [];

        public IReadOnlyList<ConversationThread> Conversations => _conversations;

        public event EventHandler? ConversationsChanged;

        public ConversationThread GetOrCreateConversation(AprsAddress peer)
        {
            ConversationThread thread = new(peer);
            _conversations.Add(thread);
            ConversationsChanged?.Invoke(this, EventArgs.Empty);
            return thread;
        }

        public Task SendAsync(AprsAddress addressee, string text, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public void SetActiveConversation(AprsAddress? peer)
        {
        }

        public void ClearActiveConversation(AprsAddress peer)
        {
        }
    }

    private sealed class TestSymbolBitmapProvider : IAprsSymbolBitmapProvider
    {
        public SKBitmap GetSymbolBitmap(Symbol symbol) => new(8, 8);
        public SKBitmap GetOverlayBitmap(SymbolCode overlayChar) => new(8, 8);
        public void Dispose()
        {
        }
    }

    private sealed class TestConfirmationDialogService : IConfirmationDialogService
    {
        public Task<bool> ShowConfirmationAsync(string title, string message) => Task.FromResult(true);
    }
}
