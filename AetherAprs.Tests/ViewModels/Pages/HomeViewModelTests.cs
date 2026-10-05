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
using AetherAprs.ViewModels;
using AetherAprs.ViewModels.Components;
using Geo;
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
        var port = CreatePort(isEnabled: false, isTx: true);
        var portService = new TestPortService(port);
        var beaconService = new TestBeaconService();
        var locationTracking = new TestLocationTrackingService();
        var viewModel = CreateViewModel(portService, beaconService, locationTracking);
        locationTracking.SetCurrentLocation(CreateLocation(41.41764, -8.52170));

        port.IsEnabled = true;
        portService.RaisePortsChanged();
        var packet = await portService.PacketSent.Task.WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
        portService.RaisePortsChanged();

        Assert.Equal(port.Id, portService.LastSentPortId);
        Assert.Equal(41.41764, packet.Location.Latitude);
        Assert.Equal(-8.52170, packet.Location.Longitude);
        Assert.Equal("N0CALL", packet.Source.Base);
        Assert.Equal(SymbolCode.LeftSquareBracket, packet.Symbol.Code);
        Assert.Contains("Initial beacon sent", viewModel.BeaconStatus);
        Assert.Equal(1, portService.SendCount);
    }

    [Fact]
    public async Task EnablingTxPortUsesGlobalDefaultSymbol()
    {
        var port = CreatePort(isEnabled: false, isTx: true);
        var portService = new TestPortService(port);
        var configuration = new TestConfigurationService();
        configuration.Settings.Aprs.Callsign = "N0CALL";
        configuration.Settings.Aprs.SymbolTable =
            AetherAprs.Models.Aprs.SymbolTable.Alternate;
        configuration.Settings.Aprs.SymbolCode = SymbolCode.GreaterThanSign;
        var locationTracking = new TestLocationTrackingService();
        var viewModel = CreateViewModel(portService, new TestBeaconService(), locationTracking, configuration);
        locationTracking.SetCurrentLocation(CreateLocation(41.41764, -8.52170));

        port.IsEnabled = true;
        portService.RaisePortsChanged();
        var packet = await portService.PacketSent.Task.WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);

        Assert.Equal('\\', packet.Symbol.TableChar);
        Assert.Equal(SymbolCode.GreaterThanSign, packet.Symbol.Code);
    }

    [Fact]
    public async Task EnablingTxPortWithoutLocationDoesNotSend()
    {
        var port = CreatePort(isEnabled: false, isTx: true);
        var portService = new TestPortService(port);
        var viewModel = CreateViewModel(portService, new TestBeaconService(), new TestLocationTrackingService());

        port.IsEnabled = true;
        portService.RaisePortsChanged();
        await Task.Delay(50, TestContext.Current.CancellationToken);

        Assert.Equal(0, portService.SendCount);
    }

    [Fact]
    public async Task EnablingRxPortDoesNotSendInitialBeacon()
    {
        var port = CreatePort(isEnabled: false, isTx: false);
        var portService = new TestPortService(port);
        var locationTracking = new TestLocationTrackingService();
        var viewModel = CreateViewModel(portService, new TestBeaconService(), locationTracking);
        locationTracking.SetCurrentLocation(CreateLocation(41.41764, -8.52170));

        port.IsEnabled = true;
        portService.RaisePortsChanged();
        await Task.Delay(50, TestContext.Current.CancellationToken);

        Assert.Equal(0, portService.SendCount);
    }

    [Fact]
    public async Task SendManualBeaconWithoutLocationDoesNotSend()
    {
        var port = CreatePort(isEnabled: true, isTx: true);
        var portService = new TestPortService(port);
        var viewModel = CreateViewModel(portService, new TestBeaconService(), new TestLocationTrackingService());

        await viewModel.SendManualBeaconAsync();

        Assert.Equal(0, portService.SendCount);
        Assert.Equal("No location available", viewModel.BeaconStatus);
    }

    [Fact]
    public async Task SendManualBeaconWithLocationSendsBeacon()
    {
        var port = CreatePort(isEnabled: true, isTx: true);
        var portService = new TestPortService(port);
        var locationTracking = new TestLocationTrackingService();
        var viewModel = CreateViewModel(portService, new TestBeaconService(), locationTracking);
        locationTracking.SetCurrentLocation(CreateLocation(41.41764, -8.52170));

        await viewModel.SendManualBeaconAsync();

        var packet = await portService.PacketSent.Task.WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
        Assert.Equal(1, portService.SendCount);
        Assert.Equal(41.41764, packet.Location.Latitude);
        Assert.Contains("Manual beacon sent", viewModel.BeaconStatus);
    }

    [Fact]
    public async Task SendManualBeaconWithoutTxPortsDoesNotSend()
    {
        var port = CreatePort(isEnabled: true, isTx: false);
        var portService = new TestPortService(port);
        var locationTracking = new TestLocationTrackingService();
        var viewModel = CreateViewModel(portService, new TestBeaconService(), locationTracking);
        locationTracking.SetCurrentLocation(CreateLocation(41.41764, -8.52170));

        await viewModel.SendManualBeaconAsync();

        Assert.Equal(0, portService.SendCount);
        Assert.Equal("No TX ports enabled", viewModel.BeaconStatus);
    }

    [Fact]
    public void EnablingTxPortWithoutCallsign_ThrowsValidationException()
    {
        // Verify that AprsSettings validation prevents empty callsigns
        var configuration = new TestConfigurationService();
        
        Assert.Throws<ArgumentException>(() => configuration.Settings.Aprs.Callsign = string.Empty);
    }

    private static HomeViewModel CreateViewModel(
        TestPortService portService, 
        TestBeaconService beaconService,
        TestLocationTrackingService? locationTracking = null,
        TestConfigurationService? configuration = null)
    {
        var isConfigurationProvided = configuration != null;
        configuration ??= new TestConfigurationService();
        locationTracking ??= new TestLocationTrackingService();
        
        // Only set default callsign if configuration was not provided
        if (!isConfigurationProvided)
        {
            configuration.Settings.Aprs.Callsign = "N0CALL";
        }
        
        var dbContextFactory = Substitute.For<IDbContextFactory<AppDbContext>>();
        var portSettingsResolver = new AprsPortSettingsResolver(configuration);
        var packetFactory = new TestPacketFactory();
        
        var beaconTransmission = new BeaconTransmissionService(
            beaconService,
            portService,
            configuration,
            portSettingsResolver,
            packetFactory,
            NullLogger<BeaconTransmissionService>.Instance);
        
        var userLocationLayer = new UserLocationLayerService(NullLogger<UserLocationLayerService>.Instance);
        
        var mapViewModel = new MapViewModel(userLocationLayer);
        
        var packetDetailsFactory = Substitute.For<IPacketDetailsViewModelFactory>();
        var navigationService = Substitute.For<INavigationService>();
        
        return new HomeViewModel(
            locationTracking,
            beaconTransmission,
            userLocationLayer,
            portService,
            mapViewModel,
            packetDetailsFactory,
            navigationService,
            NullLogger<HomeViewModel>.Instance);
    }

    private static PortConfig CreatePort(bool isEnabled, bool isTx)
    {
        return new PortConfig
        {
            Id = Guid.NewGuid(),
            Name = "Test port",
            IsEnabled = isEnabled,
            IsRx = true,
            IsTx = isTx,
            TypeSettings = new AprsIsSettings()
        };
    }

    private static LocationData CreateLocation(double latitude, double longitude)
    {
        return new LocationData
        {
            Location = new Coordinate(latitude, longitude),
            Accuracy = 5,
            Timestamp = DateTimeOffset.UtcNow
        };
    }

    private sealed class TestConfigurationService : IConfigurationService
    {
        public AppSettings Settings { get; } = new();

        public Task SaveSettingsAsync() => Task.CompletedTask;
    }

    private sealed class TestLocationService : ILocationService
    {
        public Task<LocationData> GetCurrentLocationAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateLocation(41.41764, -8.52170));

        public bool IsLocationAvailable() => true;

        public Task<bool> RequestLocationPermissionAsync() => Task.FromResult(true);
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
            new[]
            {
                BeaconConfig.CreateWalkPreset(),
                BeaconConfig.CreateDrivePreset(),
                BeaconConfig.CreateCustomPreset()
            };

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

    private sealed class TestPortService : IPortService
    {
        public TestPortService(PortConfig port)
        {
            Ports = new[] { port };
        }

        public IReadOnlyList<PortConfig> Ports { get; }

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

        public Task StartAllEnabledPortsAsync() => Task.CompletedTask;

        public Task StopAllPortsAsync() => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class TestSymbolBitmapProvider : IAprsSymbolBitmapProvider
    {
        public SKBitmap GetSymbolBitmap(Symbol symbol)
        {
            return new SKBitmap(8, 8);
        }

        public SKBitmap GetOverlayBitmap(SymbolCode overlayChar)
        {
            return new SKBitmap(8, 8);
        }

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
