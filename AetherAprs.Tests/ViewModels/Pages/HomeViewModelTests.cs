// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
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
using AetherAprs.Services.Beaconing;
using AetherAprs.Services.Configuration;
using AetherAprs.Services.Contracts;
using AetherAprs.Services.Location;
using AetherAprs.Services.Packets;
using AetherAprs.Services.Platform;
using AetherAprs.Services.Ports;
using AetherAprs.Services.Transmission;
using AetherAprs.Services.UI;
using AetherAprs.Tests.Helpers;
using AetherAprs.ViewModels;
using AetherAprs.ViewModels.Components;
using AetherAprs.ViewModels.Pages;
using Geo;
using Mapsui.Layers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SkiaSharp;
using Xunit;

namespace AetherAprs.Tests.ViewModels.Pages;

public sealed class HomeViewModelTests : TestFixtureBase
{
    [Fact]
    public async Task EnablingTxPortSendsInitialBeaconOnce()
    {
        PortConfig port = CreatePort(isEnabled: false, isTx: true);
        TestPortService portService = new(port);
        TestBeaconService beaconService = new();
        TestLocationTrackingService locationTracking = new();
        HomeViewModel viewModel = CreateViewModel(portService, beaconService, locationTracking);
        locationTracking.SetCurrentLocation(CreateLocation(41.41764, -8.52170));

        port.IsEnabled = true;
        portService.RaisePortsChanged();
        PositionPacket packet = await portService.PacketSent.Task.WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
        portService.RaisePortsChanged();

        Assert.Equal(port.Id, portService.LastSentPortId);
        Assert.Equal(41.41764, packet.Location.Latitude);
        Assert.Equal(-8.52170, packet.Location.Longitude);
        Assert.Equal("N0CALL", packet.Source.Base);
        Assert.Equal(SymbolCode.LeftSquareBracket, packet.Symbol.Code);
        // Production uses Strings.Format("InitialBeaconSentOnPortActivation", portNames)
        // Verify the status contains the base resource text (before formatting)
        string expectedBase = Localization.Strings.Get("InitialBeaconSentOnPortActivation").Split('(')[0].Trim();
        Assert.Contains(expectedBase, viewModel.BeaconStatus);
        Assert.Equal(1, portService.SendCount);
    }

    [Fact]
    public async Task StartupEnabledTxPortSendsInitialBeaconWhenLocationArrives()
    {
        PortConfig port = CreatePort(isEnabled: true, isTx: true);
        TestPortService portService = new(port);
        TestLocationTrackingService locationTracking = new();
        _ = CreateViewModel(portService, new TestBeaconService(), locationTracking);

        locationTracking.SetCurrentLocation(CreateLocation(41.41764, -8.52170));

        PositionPacket packet = await portService.PacketSent.Task.WaitAsync(
            TimeSpan.FromSeconds(1),
            TestContext.Current.CancellationToken);

        Assert.Equal(port.Id, portService.LastSentPortId);
        Assert.Equal(41.41764, packet.Location.Latitude);
        Assert.Equal(1, portService.SendCount);
    }

    [Fact]
    public async Task StartupEnabledRxOnlyPortDoesNotSendWhenLocationArrives()
    {
        PortConfig port = CreatePort(isEnabled: true, isTx: false);
        TestPortService portService = new(port);
        TestLocationTrackingService locationTracking = new();
        _ = CreateViewModel(portService, new TestBeaconService(), locationTracking);

        locationTracking.SetCurrentLocation(CreateLocation(41.41764, -8.52170));
        await Task.Delay(50, TestContext.Current.CancellationToken);

        Assert.Equal(0, portService.SendCount);
    }

    [Fact]
    public async Task EnablingTxPortUsesGlobalDefaultSymbol()
    {
        PortConfig port = CreatePort(isEnabled: false, isTx: true);
        TestPortService portService = new(port);
        TestConfigurationService configuration = new();
        configuration.Current = configuration.Current with
        {
            Aprs = configuration.Current.Aprs with
            {
                Callsign = "N0CALL",
                SymbolTable = AetherAprs.Models.Aprs.SymbolTable.Alternate,
                SymbolCode = SymbolCode.GreaterThanSign,
            },
        };
        TestLocationTrackingService locationTracking = new();
        _ = CreateViewModel(portService, new TestBeaconService(), locationTracking, configuration);
        locationTracking.SetCurrentLocation(CreateLocation(41.41764, -8.52170));

        port.IsEnabled = true;
        portService.RaisePortsChanged();
        PositionPacket packet = await portService.PacketSent.Task.WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);

        Assert.Equal('\\', packet.Symbol.TableChar);
        Assert.Equal(SymbolCode.GreaterThanSign, packet.Symbol.Code);
    }

    [Fact]
    public async Task EnablingTxPortWithoutLocationDoesNotSend()
    {
        PortConfig port = CreatePort(isEnabled: false, isTx: true);
        TestPortService portService = new(port);
        _ = CreateViewModel(portService, new TestBeaconService(), new TestLocationTrackingService());

        port.IsEnabled = true;
        portService.RaisePortsChanged();
        await Task.Delay(50, TestContext.Current.CancellationToken);

        Assert.Equal(0, portService.SendCount);
    }

    [Fact]
    public async Task EnablingRxPortDoesNotSendInitialBeacon()
    {
        PortConfig port = CreatePort(isEnabled: false, isTx: false);
        TestPortService portService = new(port);
        TestLocationTrackingService locationTracking = new();
        _ = CreateViewModel(portService, new TestBeaconService(), locationTracking);
        locationTracking.SetCurrentLocation(CreateLocation(41.41764, -8.52170));

        port.IsEnabled = true;
        portService.RaisePortsChanged();
        await Task.Delay(50, TestContext.Current.CancellationToken);

        Assert.Equal(0, portService.SendCount);
    }

    [Fact]
    public async Task SendManualBeaconWithoutLocationDoesNotSend()
    {
        PortConfig port = CreatePort(isEnabled: true, isTx: true);
        TestPortService portService = new(port);
        HomeViewModel viewModel = CreateViewModel(portService, new TestBeaconService(), new TestLocationTrackingService());

        await viewModel.SendManualBeaconAsync();

        Assert.Equal(0, portService.SendCount);
        Assert.Equal(Localization.Strings.Get("NoLocationAvailable"), viewModel.BeaconStatus);
    }

    [Fact]
    public async Task SendManualBeaconWithLocationSendsBeacon()
    {
        PortConfig port = CreatePort(isEnabled: true, isTx: true);
        TestPortService portService = new(port);
        TestLocationTrackingService locationTracking = new();
        HomeViewModel viewModel = CreateViewModel(portService, new TestBeaconService(), locationTracking);
        locationTracking.SetCurrentLocation(CreateLocation(41.41764, -8.52170));

        await viewModel.SendManualBeaconAsync();

        PositionPacket packet = await portService.PacketSent.Task.WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
        Assert.Equal(2, portService.SendCount);
        Assert.Equal(41.41764, packet.Location.Latitude);
        Assert.Contains(Localization.Strings.Get("ManualBeaconSent"), viewModel.BeaconStatus);
    }

    [Fact]
    public async Task SendManualBeaconWithoutTxPortsDoesNotSend()
    {
        PortConfig port = CreatePort(isEnabled: true, isTx: false);
        TestPortService portService = new(port);
        TestLocationTrackingService locationTracking = new();
        HomeViewModel viewModel = CreateViewModel(portService, new TestBeaconService(), locationTracking);
        locationTracking.SetCurrentLocation(CreateLocation(41.41764, -8.52170));

        await viewModel.SendManualBeaconAsync();

        Assert.Equal(0, portService.SendCount);
        Assert.Equal(Localization.Strings.Get("NoTxPortsEnabled"), viewModel.BeaconStatus);
    }

    [Fact]
    public void UpdateMapResolution_ForwardsToLayerService()
    {
        IReceivedBeaconsMapLayerService receivedBeaconsLayer = Substitute.For<IReceivedBeaconsMapLayerService>();
        TestPortService portService = new(CreatePort(isEnabled: true, isTx: false));
        HomeViewModel viewModel = CreateViewModel(
            portService,
            new TestBeaconService(),
            receivedBeaconsLayer: receivedBeaconsLayer);

        viewModel.UpdateMapResolution(1234.5);

        receivedBeaconsLayer.Received(1).SetResolution(1234.5);
    }

    [Fact]
    public void PermissionWarning_WhenAPermissionIsDenied_ExplainsWhatIsBlocked()
    {
        PermissionAttentionService attention = new();
        using HomeViewModel viewModel = CreateViewModel(
            new TestPortService(CreatePort(isEnabled: false, isTx: false)),
            new TestBeaconService(),
            permissionAttention: attention);

        attention.Report(PermissionRequestResult.Denied(PermissionKind.Notifications));

        Assert.True(viewModel.HasPermissionWarning);
        Assert.False(viewModel.CanOpenPermissionSettings);
        Assert.Contains("Notifications", viewModel.PermissionWarningText, StringComparison.Ordinal);
        Assert.Contains("denied", viewModel.PermissionWarningText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PermissionWarning_WhenAPermissionIsBlockedInSettings_OffersTheSettingsShortcut()
    {
        PermissionAttentionService attention = new();
        IPermissionService permissions = Substitute.For<IPermissionService>();
        permissions.OpenApplicationSettingsAsync(Arg.Any<CancellationToken>()).Returns(true);
        using HomeViewModel viewModel = CreateViewModel(
            new TestPortService(CreatePort(isEnabled: false, isTx: false)),
            new TestBeaconService(),
            permissionAttention: attention,
            permissionService: permissions);

        attention.Report(PermissionRequestResult.PermanentlyDenied(PermissionKind.Bluetooth));

        Assert.True(viewModel.HasPermissionWarning);
        Assert.True(viewModel.CanOpenPermissionSettings);
        Assert.Contains("Bluetooth", viewModel.PermissionWarningText, StringComparison.Ordinal);

        await viewModel.OpenPermissionSettingsCommand.ExecuteAsync(null);

        await permissions.Received(1).OpenApplicationSettingsAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public void PermissionWarning_WhenBackgroundTrackingIsRefused_ExplainsTheLimitAndOffersSettings()
    {
        PermissionAttentionService attention = new();
        using HomeViewModel viewModel = CreateViewModel(
            new TestPortService(CreatePort(isEnabled: false, isTx: false)),
            new TestBeaconService(),
            permissionAttention: attention);

        // A plain denial: background location is only reachable from the settings
        // screen, so the shortcut must be offered even without a "permanent" answer.
        attention.Report(PermissionRequestResult.Denied(PermissionKind.BackgroundLocation));

        Assert.True(viewModel.HasPermissionWarning);
        Assert.True(viewModel.CanOpenPermissionSettings);
        Assert.Contains("Background location", viewModel.PermissionWarningText, StringComparison.Ordinal);
        Assert.Contains(
            "Tracking continues while the application is open",
            viewModel.PermissionWarningText,
            StringComparison.Ordinal);
        Assert.DoesNotContain("denied", viewModel.PermissionWarningText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PermissionWarning_WhenThePermissionIsGrantedAgain_HidesTheNotice()
    {
        PermissionAttentionService attention = new();
        using HomeViewModel viewModel = CreateViewModel(
            new TestPortService(CreatePort(isEnabled: false, isTx: false)),
            new TestBeaconService(),
            permissionAttention: attention);
        attention.Report(PermissionRequestResult.Denied(PermissionKind.Notifications));

        attention.Report(PermissionRequestResult.Granted(PermissionKind.Notifications));

        Assert.False(viewModel.HasPermissionWarning);
        Assert.False(viewModel.CanOpenPermissionSettings);
        Assert.Empty(viewModel.PermissionWarningText);
    }

    private static HomeViewModel CreateViewModel(
        TestPortService portService,
        TestBeaconService beaconService,
        TestLocationTrackingService? locationTracking = null,
        TestConfigurationService? configuration = null,
        IReceivedBeaconsMapLayerService? receivedBeaconsLayer = null,
        IPermissionAttentionReporter? permissionAttention = null,
        IPermissionService? permissionService = null)
    {
        bool isConfigurationProvided = configuration != null;
        configuration ??= new TestConfigurationService();
        locationTracking ??= new TestLocationTrackingService();

        // Only set default callsign if configuration was not provided
        if (!isConfigurationProvided)
        {
            configuration.Current = configuration.Current with
            {
                Aprs = configuration.Current.Aprs with { Callsign = "N0CALL" },
            };
        }

        AprsPortSettingsResolver portSettingsResolver = new(configuration);
        TestPacketFactory packetFactory = new();

        BeaconTransmissionService beaconTransmission = new(
            beaconService,
            portService,
            configuration,
            portSettingsResolver,
            packetFactory,
            NullLogger<BeaconTransmissionService>.Instance);

        UserLocationLayerService userLocationLayer = new(NullLogger<UserLocationLayerService>.Instance);
        receivedBeaconsLayer ??= Substitute.For<IReceivedBeaconsMapLayerService>();
        IMapTileSourceService mapTileSource = Substitute.For<IMapTileSourceService>();
        mapTileSource.CreateBaseMapLayer().Returns(Substitute.For<ILayer>());

        MapViewModel mapViewModel = new(userLocationLayer);

        IPacketDetailsViewModelFactory packetDetailsFactory = Substitute.For<IPacketDetailsViewModelFactory>();
        IObjectDetailsViewModelFactory objectDetailsFactory = Substitute.For<IObjectDetailsViewModelFactory>();
        INavigationService navigationService = Substitute.For<INavigationService>();

        return new HomeViewModel(
            locationTracking,
            beaconTransmission,
            userLocationLayer,
            receivedBeaconsLayer,
            mapTileSource,
            portService,
            mapViewModel,
            packetDetailsFactory,
            objectDetailsFactory,
            navigationService,
            SynchronousUiDispatcher.Instance,
            permissionAttention ?? new PermissionAttentionService(),
            permissionService ?? Substitute.For<IPermissionService>(),
            NullLogger<HomeViewModel>.Instance);
    }

    private static PortConfig CreatePort(bool isEnabled, bool isTx) => new()
    {
        Id = Guid.NewGuid(),
        Name = "Test port",
        IsEnabled = isEnabled,
        IsRx = true,
        IsTx = isTx,
        TypeSettings = new AprsIsSettings()
    };

    private static LocationData CreateLocation(double latitude, double longitude) => new()
    {
        Location = new Coordinate(latitude, longitude),
        Accuracy = 5,
        Timestamp = DateTimeOffset.UtcNow
    };

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

    private sealed class TestLocationTrackingService : ILocationTrackingService
    {
        private LocationData? _currentLocation;

        public LocationData? CurrentLocation => _currentLocation;
        public bool IsTracking => false;
        public bool IsLocationAvailable => true;

#pragma warning disable CS0067
        public event EventHandler<LocationData>? LocationUpdated;
#pragma warning restore CS0067

        public void SetCurrentLocation(LocationData location)
        {
            _currentLocation = location;
            LocationUpdated?.Invoke(this, location);
        }

        public Task StartTrackingAsync() => Task.CompletedTask;
        public Task RequestPermissionAndStartTrackingAsync() => Task.CompletedTask;
        public void StopTracking() { }
        public void Dispose() { }
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

        public void ReportBeaconTransmissionResult(bool success)
        {
        }

        public Task SetActiveModeAsync(DynamicBeaconMode mode, CancellationToken cancellationToken = default)
        {
            CurrentConfiguration = mode switch
            {
                DynamicBeaconMode.Walk => BeaconConfig.CreateWalkPreset(),
                DynamicBeaconMode.Drive => BeaconConfig.CreateDrivePreset(),
                DynamicBeaconMode.Custom => BeaconConfig.CreateCustomPreset(),
                _ => CurrentConfiguration
            };
            return Task.CompletedTask;
        }

        public Task UpdateConfigurationAsync(BeaconConfig configuration, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public void ProcessLocationUpdate(LocationData currentLocation)
        {
        }

        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class TestPortService(PortConfig port) : IPortService
    {
        public IReadOnlyList<PortConfig> Ports { get; } = [port];

        public event EventHandler? PortsChanged;

#pragma warning disable CS0067 // Event is never used - this is a test stub
        public event EventHandler<PortPacketReceivedEventArgs>? PacketReceived;
#pragma warning restore CS0067

        public TaskCompletionSource<PositionPacket> PacketSent { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Guid? LastSentPortId { get; private set; }

        public int SendCount { get; private set; }

        public void RaisePortsChanged() => PortsChanged?.Invoke(this, EventArgs.Empty);

        public Task AddPortAsync(PortConfig port) => Task.CompletedTask;

        public Task UpdatePortAsync(PortConfig port) => Task.CompletedTask;

        public Task RemovePortAsync(Guid id) => Task.CompletedTask;

        public Task SetPortEnabledAsync(Guid id, bool enabled) => Task.CompletedTask;

        public Task SetPortShowOnMapAsync(Guid id, bool showOnMap) => Task.CompletedTask;

        public Task SendPacketAsync(Guid id, AprsPacket packet)
        {
            LastSentPortId = id;
            SendCount++;
            PacketSent.TrySetResult((PositionPacket)packet);
            return Task.CompletedTask;
        }

        public Task StopAllPortsAsync() => Task.CompletedTask;

        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class TestSymbolBitmapProvider : IAprsSymbolBitmapProvider
    {
        public SKBitmap GetSymbolBitmap(Symbol symbol) => new(8, 8);

        public SKBitmap GetOverlayBitmap(SymbolCode overlayChar) => new(8, 8);

        public void Dispose()
        {
            // No resources to dispose
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
            string[] parts = callsign.Split('-');
            Callsign source = parts.Length > 1
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
            AprsAddress addressee,
            string messageText,
            int? messageNumber = null)
        {
            string[] parts = sourceCallsign.Split('-');
            Callsign source = parts.Length > 1
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

        public MessageAckPacket CreateAckPacket(
            string sourceCallsign,
            AprsAddress addressee,
            int messageNumber)
        {
            string[] parts = sourceCallsign.Split('-');
            Callsign source = parts.Length > 1
                ? new Callsign(parts[0], int.Parse(parts[1]))
                : new Callsign(parts[0]);

            return new MessageAckPacket
            {
                Source = source,
                Destination = new Callsign("APRS"),
                Addressee = addressee,
                MessageNumber = messageNumber
            };
        }

        public MessageRejPacket CreateRejPacket(
            string sourceCallsign,
            AprsAddress addressee,
            int messageNumber)
        {
            string[] parts = sourceCallsign.Split('-');
            Callsign source = parts.Length > 1
                ? new Callsign(parts[0], int.Parse(parts[1]))
                : new Callsign(parts[0]);

            return new MessageRejPacket
            {
                Source = source,
                Destination = new Callsign("APRS"),
                Addressee = addressee,
                MessageNumber = messageNumber
            };
        }

        public ObjectPacket CreateObjectPacket(
            string sourceCallsign,
            string name,
            Coordinate location,
            SymbolTable symbolTable,
            SymbolCode symbolCode,
            bool isLive = true,
            string? comment = null)
        {
            string[] parts = sourceCallsign.Split('-');
            Callsign source = parts.Length > 1
                ? new Callsign(parts[0], int.Parse(parts[1]))
                : new Callsign(parts[0]);

            return new ObjectPacket
            {
                Source = source,
                Destination = new Callsign("APRS"),
                Name = name,
                IsLive = isLive,
                Location = location,
                Symbol = new Symbol(symbolTable, symbolCode),
                Comment = comment
            };
        }

        public ItemPacket CreateItemPacket(
            string sourceCallsign,
            string name,
            Coordinate location,
            SymbolTable symbolTable,
            SymbolCode symbolCode,
            bool isLive = true,
            string? comment = null)
        {
            string[] parts = sourceCallsign.Split('-');
            Callsign source = parts.Length > 1
                ? new Callsign(parts[0], int.Parse(parts[1]))
                : new Callsign(parts[0]);

            return new ItemPacket
            {
                Source = source,
                Destination = new Callsign("APRS"),
                Name = name,
                IsLive = isLive,
                Location = location,
                Symbol = new Symbol(symbolTable, symbolCode),
                Comment = comment
            };
        }

        public BulletinPacket CreateBulletinPacket(
            string sourceCallsign,
            string name,
            string text,
            int? messageNumber = null)
        {
            string[] parts = sourceCallsign.Split('-');
            Callsign source = parts.Length > 1
                ? new Callsign(parts[0], int.Parse(parts[1]))
                : new Callsign(parts[0]);

            return new BulletinPacket
            {
                Source = source,
                Destination = new Callsign("APRS"),
                Name = name,
                Text = text,
                MessageNumber = messageNumber
            };
        }
    }
}
