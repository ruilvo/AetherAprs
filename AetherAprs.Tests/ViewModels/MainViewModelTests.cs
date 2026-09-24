// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using AetherAprs.Configuration;
using AetherAprs.Data;
using AetherAprs.Imaging;
using AetherAprs.Models;
using AetherAprs.Models.Aprs;
using AetherAprs.Models.Messaging;
using AetherAprs.Services;
using AetherAprs.Tests.Helpers;
using AetherAprs.ViewModels;
using AetherAprs.ViewModels.Pages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SkiaSharp;
using Xunit;

namespace AetherAprs.Tests.ViewModels;

public sealed class MainViewModelTests
{
    [Fact]
    public void Constructor_NavigatesToHome()
    {
        var navigation = new TestNavigationService();
        var fixture = CreateFixture(navigation);

        Assert.Equal(typeof(HomeViewModel), navigation.LastNavigatedType);
        Assert.Equal(0, fixture.SelectedTabIndex);
        Assert.False(fixture.IsOverlayVisible);
    }

    [Fact]
    public void SelectedTabIndex_WithoutOverlay_NavigatesToMappedTab()
    {
        var navigation = new TestNavigationService();
        var fixture = CreateFixture(navigation);

        fixture.SelectedTabIndex = 2;

        Assert.Equal(typeof(PortsViewModel), navigation.LastNavigatedType);
    }

    [Fact]
    public void SelectedTabIndex_WithOverlay_DoesNotNavigate()
    {
        var navigation = new TestNavigationService();
        var fixture = CreateFixture(navigation);
        navigation.RaiseCurrentChanged(new OverlayStubViewModel());
        navigation.LastNavigatedType = null;

        fixture.SelectedTabIndex = 1;

        Assert.Null(navigation.LastNavigatedType);
        Assert.True(fixture.IsOverlayVisible);
    }

    [Fact]
    public void NavigationToTabType_ClearsOverlayAndSetsTabIndex()
    {
        var navigation = new TestNavigationService();
        var fixture = CreateFixture(navigation);
        navigation.RaiseCurrentChanged(new OverlayStubViewModel());

        navigation.RaiseCurrentChanged(fixture.SettingsViewModel);

        Assert.Null(fixture.OverlayViewModel);
        Assert.False(fixture.IsOverlayVisible);
        Assert.Equal(3, fixture.SelectedTabIndex);
    }

    [Fact]
    public void NavigationToNonTab_SetsOverlay()
    {
        var navigation = new TestNavigationService();
        var fixture = CreateFixture(navigation);
        var overlay = new OverlayStubViewModel();

        navigation.RaiseCurrentChanged(overlay);

        Assert.Same(overlay, fixture.OverlayViewModel);
        Assert.True(fixture.IsOverlayVisible);
    }

    private static MainViewModel CreateFixture(TestNavigationService navigation)
    {
        var portService = new EmptyPortService();
        var configuration = new TestConfigurationService();
        var messageService = new FakeMessageService();
        var services = new ServiceCollection();
        services.AddSingleton<IMessageService>(messageService);
        services.AddSingleton<INavigationService>(navigation);
        services.AddLogging();
        services.AddTransient<ConversationViewModel>();
        var provider = services.BuildServiceProvider();

        var symbolProvider = new TestSymbolBitmapProvider();
        var receivedBeacons = new ReceivedBeaconsViewModel(portService, symbolProvider, NullLogger<ReceivedBeaconsViewModel>.Instance);
        var locationTracking = new LocationTrackingViewModel(
            new TestLocationService(),
            NullLogger<LocationTrackingViewModel>.Instance);
        var beaconService = new TestBeaconService();
        var beaconTransmission = new BeaconTransmissionViewModel(
            beaconService,
            portService,
            configuration,
            new AprsPortSettingsResolver(configuration),
            NullLogger<BeaconTransmissionViewModel>.Instance);

        var home = new HomeViewModel(
            portService,
            receivedBeacons,
            locationTracking,
            beaconTransmission,
            NullLogger<HomeViewModel>.Instance);
        var messages = new MessagesViewModel(messageService, navigation, provider);
        
        using var tempDb = TempAppDatabase.CreateEmpty();
        var packets = new PacketsViewModel(
            tempDb.Factory,
            navigation,
            provider,
            portService,
            NullLogger<PacketsViewModel>.Instance);
        var ports = new PortsViewModel(portService, configuration, navigation, NullLogger<PortsViewModel>.Instance);
        var settings = new SettingsViewModel(configuration, navigation);

        return new MainViewModel(navigation, home, messages, packets, ports, settings);
    }

    private sealed class OverlayStubViewModel : ViewModelBase;

    private sealed class TestNavigationService : INavigationService
    {
        public ViewModelBase? CurrentViewModel { get; private set; }
        public Type? LastNavigatedType { get; set; }
        public bool CanGoBack => false;

        public event EventHandler<ViewModelBase?>? CurrentViewModelChanged;
#pragma warning disable CS0067
        public event EventHandler? RequestAppExit;
#pragma warning restore CS0067

        public void NavigateTo<TViewModel>() where TViewModel : ViewModelBase
        {
            LastNavigatedType = typeof(TViewModel);
        }

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
        public IReadOnlyList<PortConfig> Ports { get; } = Array.Empty<PortConfig>();
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
        public Task StartAllEnabledPortsAsync() => Task.CompletedTask;
        public Task StopAllPortsAsync() => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class TestConfigurationService : IConfigurationService
    {
        public AppSettings Settings { get; } = new();
        public Task SaveSettingsAsync() => Task.CompletedTask;
    }

    private sealed class TestLocationService : ILocationService
    {
        public Task<LocationData> GetCurrentLocationAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new LocationData
            {
                Latitude = 0,
                Longitude = 0,
                Accuracy = 1,
                Timestamp = DateTimeOffset.UtcNow
            });

        public bool IsLocationAvailable() => true;
        public Task<bool> RequestLocationPermissionAsync() => Task.FromResult(true);
    }

    private sealed class TestBeaconService : IBeaconService
    {
        public BeaconConfig CurrentConfiguration { get; private set; } = BeaconConfig.CreateWalkPreset();
        public IReadOnlyList<BeaconConfig> AllConfigurations =>
        [
            BeaconConfig.CreateWalkPreset(),
            BeaconConfig.CreateDrivePreset(),
            BeaconConfig.CreateCustomPreset()
        ];

        public void SetActiveMode(DynamicBeaconMode mode)
        {
            CurrentConfiguration = mode switch
            {
                DynamicBeaconMode.Walk => BeaconConfig.CreateWalkPreset(),
                DynamicBeaconMode.Drive => BeaconConfig.CreateDrivePreset(),
                DynamicBeaconMode.Custom => BeaconConfig.CreateCustomPreset(),
                _ => CurrentConfiguration
            };
        }

        public void UpdateConfiguration(BeaconConfig configuration)
        {
        }

        public BeaconTransmitDecision EvaluateLocationUpdate(LocationData currentLocation, LocationData? previousLocation) =>
            new() { ShouldTransmit = false };

        public void ResetTransmissionTimer()
        {
        }

        public PositionPacket CreatePositionPacket(
            LocationData location,
            string callsign,
            string symbolTableCharacter = "/",
            string symbolCodeCharacter = "[") =>
            new()
            {
                Source = new Callsign(callsign),
                Destination = new Callsign("APRS"),
                Latitude = location.Latitude,
                Longitude = location.Longitude,
                Precision = 2,
                Symbol = new Symbol(SymbolTable.Primary, SymbolCode.LeftSquareBracket)
            };
    }

    private sealed class FakeMessageService : IMessageService
    {
        public ObservableCollection<ConversationThread> Conversations { get; } = new();

        public ConversationThread GetOrCreateConversation(Callsign peer)
        {
            var thread = new ConversationThread(peer);
            Conversations.Add(thread);
            return thread;
        }

        public Task SendAsync(Callsign addressee, string text, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class TestSymbolBitmapProvider : IAprsSymbolBitmapProvider
    {
        public SKBitmap GetSymbolBitmap(Symbol symbol) => new(8, 8);
        public SKBitmap GetOverlayBitmap(SymbolCode overlayChar) => new(8, 8);
        public void Dispose()
        {
        }
    }
}
