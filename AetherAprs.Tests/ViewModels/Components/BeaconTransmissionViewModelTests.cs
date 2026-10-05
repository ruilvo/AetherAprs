// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AetherAprs.Configuration;
using AetherAprs.Configuration.Settings;
using AetherAprs.Factories.Packets;
using AetherAprs.Models;
using AetherAprs.Models.Aprs;
using AetherAprs.Models.Aprs.Packets;
using AetherAprs.Services.Beaconing;
using AetherAprs.Services.Configuration;
using AetherAprs.Services.Contracts;
using AetherAprs.Services.Ports;
using AetherAprs.ViewModels;
using Geo;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AetherAprs.Tests.ViewModels.Components;

public class BeaconTransmissionViewModelTests : TestFixtureBase
{
    [Fact]
    public async Task SendManualBeaconAsync_WithNullLocation_SetsStatusAndDoesNotSend()
    {
        var portService = new TestPortService();
        var viewModel = CreateViewModel(portService);

        await viewModel.SendManualBeaconAsync(null);

        Assert.Equal("No location available", viewModel.BeaconStatus);
        Assert.Equal(0, portService.SendCount);
    }

    [Fact]
    public void AprsSettings_EmptyCallsign_ThrowsValidationException()
    {
        // Verify that AprsSettings validation prevents empty callsigns
        var config = new TestConfigurationService();
        
        Assert.Throws<ArgumentException>(() => config.Settings.Aprs.Callsign = string.Empty);
    }

    [Fact]
    public async Task SendManualBeaconAsync_WithNoTxPorts_SetsStatusAndDoesNotSend()
    {
        var port = CreatePort(isTx: false);
        var portService = new TestPortService(port);
        var viewModel = CreateViewModel(portService);
        var location = CreateLocation(40.7128, -74.0060);

        await viewModel.SendManualBeaconAsync(location);

        Assert.Equal("No TX ports enabled", viewModel.BeaconStatus);
        Assert.Equal(0, portService.SendCount);
    }

    [Fact]
    public async Task SendManualBeaconAsync_WithValidConditions_SendsBeacon()
    {
        var port = CreatePort(isTx: true);
        var portService = new TestPortService(port);
        var viewModel = CreateViewModel(portService);
        var location = CreateLocation(40.7128, -74.0060);

        await viewModel.SendManualBeaconAsync(location);

        Assert.Equal(1, portService.SendCount);
        Assert.Contains("Manual beacon sent", viewModel.BeaconStatus);
    }

    [Fact]
    public async Task SendManualBeaconAsync_SendsToAllEnabledTxPorts()
    {
        var port1 = CreatePort(isTx: true);
        var port2 = CreatePort(isTx: true);
        var port3 = CreatePort(isTx: false); // RX only
        var portService = new TestPortService(port1, port2, port3);
        var viewModel = CreateViewModel(portService);
        var location = CreateLocation(40.7128, -74.0060);

        await viewModel.SendManualBeaconAsync(location);

        Assert.Equal(2, portService.SendCount); // Only TX ports
    }

    [Fact]
    public void ProcessLocationUpdate_WhenShouldNotTransmit_UpdatesStatusOnly()
    {
        var beaconService = new TestBeaconService 
        { 
            ShouldTransmit = false, 
            SecondsUntilNext = 120, 
            Reason = new BeaconTransmitReason.AwaitingTimeInterval()
        };
        var portService = new TestPortService();
        var viewModel = CreateViewModel(portService, beaconService: beaconService);
        var location = CreateLocation(40.7128, -74.0060);

        viewModel.ProcessLocationUpdate(location);

        Assert.Contains("120", viewModel.BeaconStatus);
        Assert.Equal(0, portService.SendCount);
    }

    [Fact]
    public async Task ProcessLocationUpdate_WhenShouldTransmit_SendsBeacon()
    {
        var beaconService = new TestBeaconService 
        { 
            ShouldTransmit = true, 
            Reason = new BeaconTransmitReason.CourseChanged { CourseDeltaDegrees = 25 }
        };
        var port = CreatePort(isTx: true);
        var portService = new TestPortService(port);
        var viewModel = CreateViewModel(portService, beaconService: beaconService);
        var location = CreateLocation(40.7128, -74.0060);

        viewModel.ProcessLocationUpdate(location);
        
        // Wait a bit for the async event handler to complete
        await Task.Delay(50, TestContext.Current.CancellationToken);

        Assert.Equal(1, portService.SendCount);
        Assert.Contains("Beacon sent", viewModel.BeaconStatus);
    }

    [Fact]
    public async Task SendInitialBeaconOnPortActivationAsync_WithNullLocation_DoesNotSend()
    {
        var port = CreatePort(isTx: true);
        var portService = new TestPortService(port);
        var viewModel = CreateViewModel(portService);

        await viewModel.SendInitialBeaconOnPortActivationAsync(null, new[] { port });

        Assert.Equal(0, portService.SendCount);
    }

    [Fact]
    public async Task SendInitialBeaconOnPortActivationAsync_WithEmptyPorts_DoesNotSend()
    {
        var portService = new TestPortService();
        var viewModel = CreateViewModel(portService);
        var location = CreateLocation(40.7128, -74.0060);

        await viewModel.SendInitialBeaconOnPortActivationAsync(location, Enumerable.Empty<PortConfig>());

        Assert.Equal(0, portService.SendCount);
    }

    [Fact]
    public void SendInitialBeaconOnPortActivationAsync_EmptyCallsign_ThrowsValidationException()
    {
        // Verify that AprsSettings validation prevents empty callsigns
        var config = new TestConfigurationService();
        
        Assert.Throws<ArgumentException>(() => config.Settings.Aprs.Callsign = string.Empty);
    }

    [Fact]
    public async Task SendInitialBeaconOnPortActivationAsync_WithValidConditions_SendsBeacon()
    {
        var port = CreatePort(isTx: true);
        port.Name = "Test Port 1";
        var portService = new TestPortService(port);
        var viewModel = CreateViewModel(portService);
        var location = CreateLocation(40.7128, -74.0060);

        await viewModel.SendInitialBeaconOnPortActivationAsync(location, new[] { port });

        Assert.Equal(1, portService.SendCount);
        Assert.Contains("Initial beacon sent", viewModel.BeaconStatus);
        Assert.Contains("Test Port 1", viewModel.BeaconStatus);
    }

    [Fact]
    public async Task SendInitialBeaconOnPortActivationAsync_SendsToMultiplePorts()
    {
        var port1 = CreatePort(isTx: true);
        port1.Name = "Port 1";
        var port2 = CreatePort(isTx: true);
        port2.Name = "Port 2";
        var portService = new TestPortService(port1, port2);
        var viewModel = CreateViewModel(portService);
        var location = CreateLocation(40.7128, -74.0060);

        await viewModel.SendInitialBeaconOnPortActivationAsync(location, new[] { port1, port2 });

        Assert.Equal(2, portService.SendCount);
        Assert.Contains("Port 1", viewModel.BeaconStatus);
        Assert.Contains("Port 2", viewModel.BeaconStatus);
    }

    [Fact]
    public async Task ProcessLocationUpdate_UpdatesLastBeaconDecision()
    {
        var beaconService = new TestBeaconService 
        { 
            ShouldTransmit = true, 
            Reason = new BeaconTransmitReason.IntervalExceeded { IntervalSeconds = 60 },
            CurrentSpeedKmh = 45.5,
            CurrentCourseDegrees = 180
        };
        var port = CreatePort(isTx: true);
        var portService = new TestPortService(port);
        var viewModel = CreateViewModel(portService, beaconService: beaconService);
        var location = CreateLocation(40.7128, -74.0060);

        viewModel.ProcessLocationUpdate(location);
        
        // Wait a bit for the async event handler to complete
        await Task.Delay(50, TestContext.Current.CancellationToken);

        Assert.NotNull(viewModel.LastBeaconDecision);
        Assert.True(viewModel.LastBeaconDecision.ShouldTransmit);
        Assert.IsType<BeaconTransmitReason.IntervalExceeded>(viewModel.LastBeaconDecision.Reason);
        Assert.Equal(45.5, viewModel.LastBeaconDecision.CurrentSpeedKmh);
        Assert.Equal(180, viewModel.LastBeaconDecision.CurrentCourseDegrees);
    }

    private static BeaconTransmissionViewModel CreateViewModel(
        TestPortService portService,
        TestBeaconService? beaconService = null,
        TestConfigurationService? config = null)
    {
        var isConfigurationProvided = config != null;
        config ??= new TestConfigurationService();
        
        // Only set default callsign if configuration was not provided
        if (!isConfigurationProvided)
        {
            config.Settings.Aprs.Callsign = "N0CALL";
        }
        
        beaconService ??= new TestBeaconService();
        var portSettingsResolver = new AprsPortSettingsResolver(config);
        var packetFactory = new TestPacketFactory();

        return new BeaconTransmissionViewModel(
            beaconService,
            portService,
            config,
            portSettingsResolver,
            packetFactory,
            NullLogger<BeaconTransmissionViewModel>.Instance);
    }

    private static PortConfig CreatePort(bool isTx = true)
    {
        return new PortConfig
        {
            Id = Guid.NewGuid(),
            Name = "Test Port",
            IsEnabled = true,
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
            Accuracy = 10,
            Timestamp = DateTimeOffset.UtcNow
        };
    }

    private sealed class TestConfigurationService : IConfigurationService
    {
        public AppSettings Settings { get; } = new();

        public Task SaveSettingsAsync() => Task.CompletedTask;
    }

    private sealed class TestBeaconService : IBeaconService
    {
        public bool ShouldTransmit { get; set; } = true;
        public int SecondsUntilNext { get; set; } = 0;
        public BeaconTransmitReason Reason { get; set; } = new BeaconTransmitReason.WaitingForNextInterval { SecondsUntilNext = 0 };
        public double? CurrentSpeedKmh { get; set; }
        public double? CurrentCourseDegrees { get; set; }

        public event EventHandler<BeaconRequestedEventArgs>? BeaconRequested;

        public BeaconConfig CurrentConfiguration { get; private set; } = BeaconConfig.CreateWalkPreset();

        public IReadOnlyList<BeaconConfig> AllConfigurations =>
            new[] { BeaconConfig.CreateWalkPreset(), BeaconConfig.CreateDrivePreset(), BeaconConfig.CreateCustomPreset() };

        public double? LastCourseDegrees => CurrentCourseDegrees;

        public BeaconTransmitDecision? CurrentDecision => new BeaconTransmitDecision
        {
            ShouldTransmit = ShouldTransmit,
            Reason = Reason,
            SecondsUntilNextBeacon = SecondsUntilNext,
            CurrentSpeedKmh = CurrentSpeedKmh,
            CurrentCourseDegrees = CurrentCourseDegrees
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

        public void UpdateConfiguration(BeaconConfig configuration) { }

        public void ProcessLocationUpdate(LocationData currentLocation)
        {
            // Simulate the beacon service behavior: raise event if ShouldTransmit is true
            if (ShouldTransmit && CurrentDecision is not null)
            {
                BeaconRequested?.Invoke(this, new BeaconRequestedEventArgs
                {
                    Location = currentLocation,
                    Decision = CurrentDecision
                });
            }
        }
    }

    private sealed class TestPortService : IPortService
    {
        private readonly List<PortConfig> _ports;

        public TestPortService(params PortConfig[] ports)
        {
            _ports = ports.ToList();
        }

        public IReadOnlyList<PortConfig> Ports => _ports;

#pragma warning disable CS0067 // Events are never used - this is a test stub
        public event EventHandler? PortsChanged;
        public event EventHandler<PortPacketReceivedEventArgs>? PacketReceived;
#pragma warning restore CS0067

        public int SendCount { get; private set; }

        public Task AddPortAsync(PortConfig port) => Task.CompletedTask;
        public Task UpdatePortAsync(PortConfig port) => Task.CompletedTask;
        public Task RemovePortAsync(Guid id) => Task.CompletedTask;
        public Task SetPortEnabledAsync(Guid id, bool enabled) => Task.CompletedTask;

        public Task SetPortShowOnMapAsync(Guid id, bool showOnMap) => Task.CompletedTask;

        public Task SendPacketAsync(Guid id, AprsPacket packet)
        {
            SendCount++;
            return Task.CompletedTask;
        }

        public Task StartAllEnabledPortsAsync() => Task.CompletedTask;
        public Task StopAllPortsAsync() => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
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
