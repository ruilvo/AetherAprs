// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AetherAprs.Configuration;
using AetherAprs.Models.Aprs;
using AetherAprs.Services;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace AetherAprs.Tests.Services;

public sealed class MessageServiceTests
{
    [Fact]
    public async Task SendAsync_SendsMessagePacketOnEnabledTxPorts_AndStoresOutbound()
    {
        var port = new PortConfig
        {
            Id = Guid.NewGuid(),
            Name = "TX",
            IsEnabled = true,
            IsTx = true,
            IsRx = true,
            TypeSettings = new AprsIsSettings()
        };
        var portService = new FakePortService(port);
        var configuration = CreateConfiguration("N0CALL", defaultSsid: 1);
        var resolver = Substitute.For<IAprsPortSettingsResolver>();
        resolver.GetPortCallsign(port, "N0CALL").Returns("N0CALL-1");

        var service = new MessageService(
            portService,
            configuration,
            resolver,
            Substitute.For<ILogger<MessageService>>());

        var addressee = new Callsign("K0OTH", 7);
        await service.SendAsync(addressee, "Hello");

        var message = Assert.IsType<MessagePacket>(Assert.Single(portService.SentPackets));
        Assert.Equal(new Callsign("N0CALL", 1), message.Source);
        Assert.Equal(addressee, message.Addressee);
        Assert.Equal("Hello", message.Text);
        Assert.NotNull(message.MessageNumber);

        var thread = Assert.Single(service.Conversations);
        Assert.Equal(addressee, thread.Peer);
        var stored = Assert.Single(thread.Messages);
        Assert.True(stored.IsOutbound);
        Assert.Equal("Hello", stored.Text);
    }

    [Fact]
    public void PacketReceived_StoresInboundMessageAddressedToUs()
    {
        var portService = new FakePortService();
        var configuration = CreateConfiguration("N0CALL");
        var service = new MessageService(
            portService,
            configuration,
            Substitute.For<IAprsPortSettingsResolver>(),
            Substitute.For<ILogger<MessageService>>());

        portService.RaisePacketReceived(new PortPacketReceivedEventArgs
        {
            PortId = Guid.NewGuid(),
            Packet = new MessagePacket
            {
                Source = new Callsign("K0PEER", 1),
                Destination = new Callsign("APRS"),
                Addressee = new Callsign("N0CALL"),
                Text = "Ping"
            }
        });

        var thread = Assert.Single(service.Conversations);
        Assert.Equal(new Callsign("K0PEER", 1), thread.Peer);
        var stored = Assert.Single(thread.Messages);
        Assert.False(stored.IsOutbound);
        Assert.Equal("Ping", stored.Text);
    }

    private static IConfigurationService CreateConfiguration(string callsign, int defaultSsid = 0)
    {
        var configuration = Substitute.For<IConfigurationService>();
        configuration.Settings.Returns(new AppSettings
        {
            Aprs = new AprsSettings { Callsign = callsign, DefaultSsid = defaultSsid }
        });
        return configuration;
    }

    private sealed class FakePortService : IPortService
    {
        private readonly List<PortConfig> _ports;

        public FakePortService(params PortConfig[] ports)
        {
            _ports = [.. ports];
        }

        public IReadOnlyList<PortConfig> Ports => _ports;
        public event EventHandler? PortsChanged;
        public event EventHandler<PortPacketReceivedEventArgs>? PacketReceived;
        public List<AprsPacket> SentPackets { get; } = [];

        public void RaisePacketReceived(PortPacketReceivedEventArgs args) =>
            PacketReceived?.Invoke(this, args);

        public Task SendPacketAsync(Guid id, AprsPacket packet)
        {
            SentPackets.Add(packet);
            return Task.CompletedTask;
        }

        public Task AddPortAsync(PortConfig port) => Task.CompletedTask;
        public Task UpdatePortAsync(PortConfig port) => Task.CompletedTask;
        public Task RemovePortAsync(Guid id) => Task.CompletedTask;
        public Task SetPortEnabledAsync(Guid id, bool enabled) => Task.CompletedTask;
        public Task SetPortShowOnMapAsync(Guid id, bool showOnMap) => Task.CompletedTask;
        public Task StartAllEnabledPortsAsync() => Task.CompletedTask;
        public Task StopAllPortsAsync() => Task.CompletedTask;
    }
}
