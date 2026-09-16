// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AetherAprs.Configuration;
using AetherAprs.Data;
using AetherAprs.Models;
using AetherAprs.Models.Aprs;
using AetherAprs.Services;
using AetherAprs.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace AetherAprs.Tests.Data;

public sealed class SavedDataPersistenceTests
{
    [Fact]
    public void BeaconServicePersistsActiveModeAndConfiguration()
    {
        using var db = TempAppDatabase.Create();
        var service = new BeaconService(db.Factory, Substitute.For<ILogger<BeaconService>>());
        service.UpdateConfiguration(BeaconConfig.CreateWalkPreset() with { SlowIntervalSeconds = 1111 });
        service.SetActiveMode(DynamicBeaconMode.Drive);

        var reloaded = new BeaconService(db.Factory, Substitute.For<ILogger<BeaconService>>());
        Assert.Equal(DynamicBeaconMode.Drive, reloaded.CurrentConfiguration.Mode);
        Assert.Equal(1111, reloaded.AllConfigurations.Single(config => config.Mode == DynamicBeaconMode.Walk).SlowIntervalSeconds);
    }

    [Fact]
    public async Task MessageServicePersistsHistoryAcrossRestart()
    {
        using var db = TempAppDatabase.Create();
        var portService = new FakePortService();
        var configuration = Substitute.For<IConfigurationService>();
        configuration.Settings.Returns(new AppSettings
        {
            Aprs = new AprsSettings { Callsign = "N0CALL" }
        });

        using (var service = new MessageService(
                   portService,
                   configuration,
                   Substitute.For<IAprsPortSettingsResolver>(),
                   Substitute.For<ILogger<MessageService>>(),
                   db.Factory))
        {
            portService.RaisePacketReceived(new PortPacketReceivedEventArgs
            {
                PortId = Guid.NewGuid(),
                Packet = new MessagePacket
                {
                    Source = new Callsign("K0PEER", 1),
                    Destination = new Callsign("APRS"),
                    Addressee = new Callsign("N0CALL"),
                    Text = "Ping",
                    MessageNumber = 9
                }
            });

            Assert.Equal("Ping", Assert.Single(Assert.Single(service.Conversations).Messages).Text);
        }

        using var reloaded = new MessageService(
            portService,
            configuration,
            Substitute.For<IAprsPortSettingsResolver>(),
            Substitute.For<ILogger<MessageService>>(),
            db.Factory);
        var thread = Assert.Single(reloaded.Conversations);
        Assert.Equal(new Callsign("K0PEER", 1), thread.Peer);
        var stored = Assert.Single(thread.Messages);
        Assert.Equal("Ping", stored.Text);
        Assert.Equal(9, stored.MessageNumber);
    }

    private sealed class FakePortService : IPortService
    {
        public IReadOnlyList<PortConfig> Ports { get; } = [];
        public event EventHandler? PortsChanged
        {
            add { }
            remove { }
        }
        public event EventHandler<PortPacketReceivedEventArgs>? PacketReceived;

        public void RaisePacketReceived(PortPacketReceivedEventArgs args) =>
            PacketReceived?.Invoke(this, args);

        public Task SendPacketAsync(Guid id, AprsPacket packet) => Task.CompletedTask;
        public Task AddPortAsync(PortConfig port) => Task.CompletedTask;
        public Task UpdatePortAsync(PortConfig port) => Task.CompletedTask;
        public Task RemovePortAsync(Guid id) => Task.CompletedTask;
        public Task SetPortEnabledAsync(Guid id, bool enabled) => Task.CompletedTask;
        public Task SetPortShowOnMapAsync(Guid id, bool showOnMap) => Task.CompletedTask;
        public Task StartAllEnabledPortsAsync() => Task.CompletedTask;
        public Task StopAllPortsAsync() => Task.CompletedTask;
    }
}
