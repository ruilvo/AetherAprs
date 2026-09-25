// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AetherAprs.Configuration;
using AetherAprs.Data;
using AetherAprs.Imaging;
using AetherAprs.Models;
using AetherAprs.Models.Aprs;
using AetherAprs.Services;
using AetherAprs.ViewModels;
using AetherAprs.ViewModels.Components;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SkiaSharp;
using Xunit;

namespace AetherAprs.Tests.ViewModels;

public sealed class HomeViewModelTests : TestFixtureBase
{
    [Fact]
    public async Task EnablingTxPortSendsInitialBeaconOnce()
    {
        var port = CreatePort(isEnabled: false, isTx: true);
        var portService = new TestPortService(port);
        var beaconService = new TestBeaconService();
        var viewModel = CreateViewModel(portService, beaconService);
        viewModel.LocationTracking.CurrentLocation = CreateLocation(41.41764, -8.52170);

        port.IsEnabled = true;
        portService.RaisePortsChanged();
        var packet = await portService.PacketSent.Task.WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
        portService.RaisePortsChanged();

        Assert.Equal(port.Id, portService.LastSentPortId);
        Assert.Equal(41.41764, packet.Latitude);
        Assert.Equal(-8.52170, packet.Longitude);
        Assert.Equal("N0CALL", packet.Source.Base);
        Assert.Equal(SymbolCode.LeftSquareBracket, packet.Symbol.Code);
        Assert.Contains("Initial beacon sent", viewModel.BeaconTransmission.BeaconStatus);
        Assert.Equal(1, portService.SendCount);
    }

    [Fact]
    public async Task EnablingTxPortUsesGlobalDefaultSymbol()
    {
        var port = CreatePort(isEnabled: false, isTx: true);
        var portService = new TestPortService(port);
        var configuration = new TestConfigurationService();
        configuration.Settings.Aprs.Callsign = "N0CALL";
        configuration.Settings.Aprs.DefaultSymbolTableCharacter = "\\";
        configuration.Settings.Aprs.DefaultSymbolCodeCharacter = ">";
        var viewModel = CreateViewModel(portService, new TestBeaconService(), configuration);
        viewModel.LocationTracking.CurrentLocation = CreateLocation(41.41764, -8.52170);

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
        var viewModel = CreateViewModel(portService, new TestBeaconService());

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
        var viewModel = CreateViewModel(portService, new TestBeaconService());
        viewModel.LocationTracking.CurrentLocation = CreateLocation(41.41764, -8.52170);

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
        var viewModel = CreateViewModel(portService, new TestBeaconService());

        await viewModel.SendManualBeaconAsync();

        Assert.Equal(0, portService.SendCount);
        Assert.Equal("No location available", viewModel.BeaconTransmission.BeaconStatus);
    }

    [Fact]
    public async Task SendManualBeaconWithLocationSendsBeacon()
    {
        var port = CreatePort(isEnabled: true, isTx: true);
        var portService = new TestPortService(port);
        var viewModel = CreateViewModel(portService, new TestBeaconService());
        viewModel.LocationTracking.CurrentLocation = CreateLocation(41.41764, -8.52170);

        await viewModel.SendManualBeaconAsync();

        var packet = await portService.PacketSent.Task.WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
        Assert.Equal(1, portService.SendCount);
        Assert.Equal(41.41764, packet.Latitude);
        Assert.Contains("Manual beacon sent", viewModel.BeaconTransmission.BeaconStatus);
    }

    [Fact]
    public async Task SendManualBeaconWithoutTxPortsDoesNotSend()
    {
        var port = CreatePort(isEnabled: true, isTx: false);
        var portService = new TestPortService(port);
        var viewModel = CreateViewModel(portService, new TestBeaconService());
        viewModel.LocationTracking.CurrentLocation = CreateLocation(41.41764, -8.52170);

        await viewModel.SendManualBeaconAsync();

        Assert.Equal(0, portService.SendCount);
        Assert.Equal("No TX ports enabled", viewModel.BeaconTransmission.BeaconStatus);
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
        TestConfigurationService? configuration = null)
    {
        var isConfigurationProvided = configuration != null;
        configuration ??= new TestConfigurationService();
        
        // Only set default callsign if configuration was not provided
        if (!isConfigurationProvided)
        {
            configuration.Settings.Aprs.Callsign = "N0CALL";
        }
        
        var dbContextFactory = Substitute.For<IDbContextFactory<AppDbContext>>();
        var symbolProvider = new TestSymbolBitmapProvider();
        var packetCache = new PacketCacheService(portService, dbContextFactory, NullLogger<PacketCacheService>.Instance);
        var receivedBeacons = new ReceivedBeaconsViewModel(
            portService, 
            packetCache, 
            configuration, 
            dbContextFactory, 
            symbolProvider, 
            NullLogger<ReceivedBeaconsViewModel>.Instance);
        var portSettingsResolver = new AprsPortSettingsResolver(configuration);
        
        var locationTracking = new LocationTrackingViewModel(
            new TestLocationService(),
            NullLogger<LocationTrackingViewModel>.Instance);
            
        var beaconTransmission = new BeaconTransmissionViewModel(
            beaconService,
            portService,
            configuration,
            portSettingsResolver,
            NullLogger<BeaconTransmissionViewModel>.Instance);
        
        var mapViewModel = new MapViewModel();
        
        return new HomeViewModel(
            portService,
            receivedBeacons,
            locationTracking,
            beaconTransmission,
            mapViewModel,
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
            Latitude = latitude,
            Longitude = longitude,
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

    private sealed class TestBeaconService : IBeaconService
    {
        public BeaconConfig CurrentConfiguration { get; private set; } = BeaconConfig.CreateWalkPreset();

        public IReadOnlyList<BeaconConfig> AllConfigurations =>
            new[]
            {
                BeaconConfig.CreateWalkPreset(),
                BeaconConfig.CreateDrivePreset(),
                BeaconConfig.CreateCustomPreset()
            };

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
            string symbolCodeCharacter = "[")
        {
            var callsignParts = callsign.Split('-');
            var source = callsignParts.Length > 1 && int.TryParse(callsignParts[1], out var ssid)
                ? new Callsign(callsignParts[0], ssid)
                : new Callsign(callsign);

            return new PositionPacket
            {
                Source = source,
                Destination = new Callsign("APRS"),
                Latitude = location.Latitude,
                Longitude = location.Longitude,
                Precision = 2,
                Symbol = new Symbol(
                    symbolTableCharacter[0].ToSymbolTable(),
                    symbolCodeCharacter[0].ToSymbolCode()),
                Comment = "Walking"
            };
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
}
