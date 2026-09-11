// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AetherAprs.Configuration;
using AetherAprs.Models;
using AetherAprs.Models.Aprs;
using AetherAprs.Services;
using AetherAprs.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AetherAprs.Tests.ViewModels;

public sealed class HomeViewModelTests
{
    [Fact]
    public async Task EnablingTxPortSendsInitialBeaconOnce()
    {
        var port = CreatePort(isEnabled: false, isTx: true);
        var portService = new TestPortService(port);
        var beaconService = new TestBeaconService();
        var viewModel = CreateViewModel(portService, beaconService);
        viewModel.UserLocation = CreateLocation(41.41764, -8.52170);

        port.IsEnabled = true;
        portService.RaisePortsChanged();
        var packet = await portService.PacketSent.Task.WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
        portService.RaisePortsChanged();

        Assert.Equal(port.Id, portService.LastSentPortId);
        Assert.Equal(41.41764, packet.Latitude);
        Assert.Equal(-8.52170, packet.Longitude);
        Assert.Equal("CT7ALW", packet.Source.Base);
        Assert.Contains("Initial beacon sent", viewModel.BeaconStatus);
        Assert.Equal(1, portService.SendCount);
    }

    [Fact]
    public async Task EnablingTxPortUsesPortSsidInBeaconSource()
    {
        var port = CreatePort(isEnabled: false, isTx: true);
        port.Ssid = 7;
        var portService = new TestPortService(port);
        var viewModel = CreateViewModel(portService, new TestBeaconService());
        viewModel.UserLocation = CreateLocation(41.41764, -8.52170);

        port.IsEnabled = true;
        portService.RaisePortsChanged();
        var packet = await portService.PacketSent.Task.WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);

        Assert.Equal("CT7ALW", packet.Source.Base);
        Assert.Equal(7, packet.Source.Ssid);
    }

    [Fact]
    public async Task EnablingRxOnlyPortDoesNotSendInitialBeacon()
    {
        var port = CreatePort(isEnabled: false, isTx: false);
        var portService = new TestPortService(port);
        var viewModel = CreateViewModel(portService, new TestBeaconService());
        viewModel.UserLocation = CreateLocation(41.41764, -8.52170);

        port.IsEnabled = true;
        portService.RaisePortsChanged();
        await Task.Delay(50, TestContext.Current.CancellationToken);

        Assert.Equal(0, portService.SendCount);
    }

    [Fact]
    public async Task EnablingTxPortWithoutLocationDoesNotSendInitialBeacon()
    {
        var port = CreatePort(isEnabled: false, isTx: true);
        var portService = new TestPortService(port);
        _ = CreateViewModel(portService, new TestBeaconService());

        port.IsEnabled = true;
        portService.RaisePortsChanged();
        await Task.Delay(50, TestContext.Current.CancellationToken);

        Assert.Equal(0, portService.SendCount);
    }

    [Fact]
    public async Task ManualBeaconSendsPacketToEnabledTxPort()
    {
        var port = CreatePort(isEnabled: true, isTx: true);
        var portService = new TestPortService(port);
        var viewModel = CreateViewModel(portService, new TestBeaconService());
        viewModel.UserLocation = CreateLocation(41.41764, -8.52170);

        await viewModel.SendManualBeaconAsync();

        var packet = await portService.PacketSent.Task.WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
        Assert.Equal(port.Id, portService.LastSentPortId);
        Assert.Equal(41.41764, packet.Latitude);
        Assert.Equal(1, portService.SendCount);
        Assert.Equal("✓ Manual beacon sent", viewModel.BeaconStatus);
    }

    [Fact]
    public async Task ManualBeaconWithoutTxPortDoesNotSend()
    {
        var port = CreatePort(isEnabled: true, isTx: false);
        var portService = new TestPortService(port);
        var viewModel = CreateViewModel(portService, new TestBeaconService());
        viewModel.UserLocation = CreateLocation(41.41764, -8.52170);

        await viewModel.SendManualBeaconAsync();

        Assert.Equal(0, portService.SendCount);
        Assert.Equal("No TX ports enabled", viewModel.BeaconStatus);
    }

    [Fact]
    public async Task EnablingTxPortWithoutCallsignDoesNotSend()
    {
        var port = CreatePort(isEnabled: false, isTx: true);
        var portService = new TestPortService(port);
        var configuration = new TestConfigurationService();
        configuration.Settings.Aprs.Callsign = string.Empty;
        var viewModel = new HomeViewModel(
            new TestLocationService(),
            new TestBeaconService(),
            portService,
            configuration,
            NullLogger<HomeViewModel>.Instance);
        viewModel.UserLocation = CreateLocation(41.41764, -8.52170);

        port.IsEnabled = true;
        portService.RaisePortsChanged();
        await Task.Delay(50, TestContext.Current.CancellationToken);

        Assert.Equal(0, portService.SendCount);
    }

    private static HomeViewModel CreateViewModel(TestPortService portService, TestBeaconService beaconService)
    {
        var configuration = new TestConfigurationService();
        configuration.Settings.Aprs.Callsign = "CT7ALW";
        return new HomeViewModel(
            new TestLocationService(),
            beaconService,
            portService,
            configuration,
            NullLogger<HomeViewModel>.Instance);
    }

    private static PortConfig CreatePort(bool isEnabled, bool isTx)
    {
        return new PortConfig
        {
            Id = Guid.NewGuid(),
            Name = "Test port",
            Type = PortType.AprsIs,
            IsEnabled = isEnabled,
            IsRx = true,
            IsTx = isTx,
            DynamicBeaconMode = DynamicBeaconMode.Walk
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
        public BeaconConfiguration CurrentConfiguration { get; private set; } = BeaconConfiguration.CreateWalkPreset();

        public IReadOnlyList<BeaconConfiguration> AllConfigurations =>
            new[]
            {
                BeaconConfiguration.CreateWalkPreset(),
                BeaconConfiguration.CreateDrivePreset(),
                BeaconConfiguration.CreateCustomPreset()
            };

        public void SetActiveMode(DynamicBeaconMode mode)
        {
            CurrentConfiguration = mode switch
            {
                DynamicBeaconMode.Walk => BeaconConfiguration.CreateWalkPreset(),
                DynamicBeaconMode.Drive => BeaconConfiguration.CreateDrivePreset(),
                DynamicBeaconMode.Custom => BeaconConfiguration.CreateCustomPreset(),
                _ => CurrentConfiguration
            };
        }

        public void UpdateCustomConfiguration(BeaconConfiguration configuration)
        {
        }

        public BeaconTransmitDecision EvaluateLocationUpdate(LocationData currentLocation, LocationData? previousLocation) =>
            new() { ShouldTransmit = false };

        public void ResetTransmissionTimer()
        {
        }

        public PositionPacket CreatePositionPacket(LocationData location, string callsign)
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
                Precision = 3,
                Symbol = new Symbol(SymbolTable.Primary, SymbolCode.LatinSmallLetterA),
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

        public TaskCompletionSource<PositionPacket> PacketSent { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Guid? LastSentPortId { get; private set; }

        public int SendCount { get; private set; }

        public void RaisePortsChanged() => PortsChanged?.Invoke(this, EventArgs.Empty);

        public Task AddPortAsync(PortConfig port) => Task.CompletedTask;

        public Task UpdatePortAsync(PortConfig port) => Task.CompletedTask;

        public Task RemovePortAsync(Guid id) => Task.CompletedTask;

        public Task SetPortEnabledAsync(Guid id, bool enabled) => Task.CompletedTask;

        public Task SendPacketAsync(Guid id, AprsPacket packet)
        {
            LastSentPortId = id;
            SendCount++;
            PacketSent.TrySetResult((PositionPacket)packet);
            return Task.CompletedTask;
        }

        public Task StartAllEnabledPortsAsync() => Task.CompletedTask;

        public Task StopAllPortsAsync() => Task.CompletedTask;
    }
}
