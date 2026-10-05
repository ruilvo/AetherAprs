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
using AetherAprs.Services.Messaging;
using AetherAprs.Services.Packets;
using AetherAprs.Services.Platform;
using AetherAprs.Services.Ports;
using AetherAprs.Services.UI;
using AetherAprs.Tests.Helpers;
using AetherAprs.ViewModels;
using Geo;
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

        Assert.Equal(typeof(PacketsViewModel), navigation.LastNavigatedType);
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
        Assert.Equal(4, fixture.SelectedTabIndex);
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
        services.AddSingleton<IConversationViewModelFactory, ConversationViewModelFactory>();
        var provider = services.BuildServiceProvider();
        var conversationFactory = provider.GetRequiredService<IConversationViewModelFactory>();

        var dbContextFactory = Substitute.For<IDbContextFactory<AppDbContext>>();
        var symbolProvider = new TestSymbolBitmapProvider();
        var packetQuery = Substitute.For<IPacketQueryService>();
        var packetStorage = Substitute.For<IPacketStorageService>();
        var receivedBeacons = new ReceivedBeaconsViewModel(
            portService, 
            packetQuery,
            packetStorage,
            configuration, 
            symbolProvider, 
            NullLogger<ReceivedBeaconsViewModel>.Instance);
        var locationTracking = new LocationTrackingViewModel(
            new TestLocationService(),
            NullLogger<LocationTrackingViewModel>.Instance);
        var beaconService = new TestBeaconService();
        var packetFactory = new TestPacketFactory();
        var beaconTransmission = new BeaconTransmissionViewModel(
            beaconService,
            portService,
            configuration,
            new AprsPortSettingsResolver(configuration),
            packetFactory,
            NullLogger<BeaconTransmissionViewModel>.Instance);

        var mapViewModel = new MapViewModel();

        var packetDetailsFactory = Substitute.For<IPacketDetailsViewModelFactory>();
        var navigationService = Substitute.For<INavigationService>();

        var home = new HomeViewModel(
            portService,
            receivedBeacons,
            locationTracking,
            beaconTransmission,
            mapViewModel,
            packetDetailsFactory,
            navigationService,
            NullLogger<HomeViewModel>.Instance);
        var messages = new MessagesViewModel(messageService, navigation, conversationFactory);
        
        var packets = new PacketsViewModel(
            packetQuery,
            packetStorage,
            navigation,
            packetDetailsFactory,
            configuration,
            NullLogger<PacketsViewModel>.Instance);
        var factory = Substitute.For<IAddEditPortViewModelFactory>();
        var ports = new PortsViewModel(portService, configuration, navigation, factory, NullLogger<PortsViewModel>.Instance, NullLoggerFactory.Instance);
        var symbolPicker = new AprsSymbolPickerViewModel(symbolProvider);
        var settings = new SettingsViewModel(configuration, navigation, symbolPicker);

        return new MainViewModel(navigation, home, messages, packets, ports, settings);
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
                Location = new Coordinate(0, 0),
                Accuracy = 1,
                Timestamp = DateTimeOffset.UtcNow
            });

        public bool IsLocationAvailable() => true;
        public Task<bool> RequestLocationPermissionAsync() => Task.FromResult(true);
    }

    private sealed class TestBeaconService : IBeaconService
    {
#pragma warning disable CS0067
        public event EventHandler<BeaconRequestedEventArgs>? BeaconRequested;
#pragma warning restore CS0067

        public BeaconConfig CurrentConfiguration { get; private set; } = BeaconConfig.CreateWalkPreset();
        public IReadOnlyList<BeaconConfig> AllConfigurations =>
        [
            BeaconConfig.CreateWalkPreset(),
            BeaconConfig.CreateDrivePreset(),
            BeaconConfig.CreateCustomPreset()
        ];

        public double? LastCourseDegrees => null;

        public BeaconTransmitDecision? CurrentDecision => null;

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

        public void ProcessLocationUpdate(LocationData currentLocation)
        {
        }
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

    private sealed class TestPacketFactory : IPacketFactory
    {
        public PositionPacket CreatePositionPacket(
            LocationData location,
            string callsign,
            SymbolTable symbolTable,
            SymbolCode symbolCode,
            double? course = null,
            string? comment = null)
        {
            var parts = callsign.Split('-');
            var source = parts.Length > 1
                ? new Callsign(parts[0], int.Parse(parts[1]))
                : new Callsign(parts[0]);

            return new PositionPacket
            {
                Source = source,
                Destination = new Callsign("APRS"),
                Location = location.Location,
                Symbol = new Symbol(symbolTable, symbolCode),
                Course = course,
                Comment = comment ?? string.Empty,
                Altitude = location.Altitude.HasValue ? (int)(location.Altitude.Value * 3.28084) : null
            };
        }

        public MessagePacket CreateMessagePacket(
            string sourceCallsign,
            Callsign addressee,
            string messageText,
            int? messageNumber = null)
        {
            var parts = sourceCallsign.Split('-');
            var source = parts.Length > 1
                ? new Callsign(parts[0], int.Parse(parts[1]))
                : new Callsign(parts[0]);

            return new MessagePacket
            {
                Source = source,
                Destination = new Callsign("APRS"),
                Addressee = addressee,
                Text = messageText,
                MessageNumber = messageNumber
            };
        }

        public MessagePacket CreateAckPacket(
            string sourceCallsign,
            Callsign addressee,
            int messageNumber)
        {
            var parts = sourceCallsign.Split('-');
            var source = parts.Length > 1
                ? new Callsign(parts[0], int.Parse(parts[1]))
                : new Callsign(parts[0]);

            return new MessagePacket
            {
                Source = source,
                Destination = new Callsign("APRS"),
                Addressee = addressee,
                Text = $"ack{messageNumber}"
            };
        }

        public MessagePacket CreateRejPacket(
            string sourceCallsign,
            Callsign addressee,
            int messageNumber)
        {
            var parts = sourceCallsign.Split('-');
            var source = parts.Length > 1
                ? new Callsign(parts[0], int.Parse(parts[1]))
                : new Callsign(parts[0]);

            return new MessagePacket
            {
                Source = source,
                Destination = new Callsign("APRS"),
                Addressee = addressee,
                Text = $"rej{messageNumber}"
            };
        }
    }
}
