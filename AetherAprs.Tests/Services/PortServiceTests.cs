// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AetherAprs.Configuration;
using AetherAprs.Extensions;
using AetherAprs.Models;
using AetherAprs.Models.Aprs;
using AetherAprs.Services;
using AetherAprs.Transports.Kiss;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AetherAprs.Tests.Services;

public sealed class PortServiceTests
{
    [Fact]
    public async Task UpdatePortAsyncPersistsAprsIsSettings()
    {
        var existingPort = new PortConfig
        {
            Id = Guid.NewGuid(),
            Name = "Old name",
            TypeSettings = new AprsIsSettings
            {
                Server = "old.example",
                ServerPort = 14580,
                Passcode = "old-passcode",
                Filter = "m/10"
            },
            Ssid = 1,
            IsRx = true,
            IsTx = false
        };
        var configuration = new TestConfigurationService(existingPort);
        var service = CreateService(configuration);
        var updatedPort = new PortConfig
        {
            Id = existingPort.Id,
            Name = "Updated name",
            IsEnabled = true,
            TypeSettings = new AprsIsSettings
            {
                Server = "new.example",
                ServerPort = 14501,
                Passcode = "new-passcode",
                Filter = "m/50"
            },
            Ssid = 2,
            IsRx = false,
            IsTx = true,
            SymbolTableCharacter = "\\",
            SymbolCodeCharacter = ">",
            DynamicBeaconMode = DynamicBeaconMode.Drive,
            ShowOnMap = false
        };

        await service.UpdatePortAsync(updatedPort);

        var aprsIsSettings = existingPort.GetAprsIsSettings();
        Assert.Equal("Updated name", existingPort.Name);
        Assert.True(existingPort.IsEnabled);
        Assert.NotNull(aprsIsSettings);
        Assert.Equal("new.example", aprsIsSettings.Server);
        Assert.Equal(14501, aprsIsSettings.ServerPort);
        Assert.Equal("new-passcode", aprsIsSettings.Passcode);
        Assert.Equal("m/50", aprsIsSettings.Filter);
        Assert.Equal(2, existingPort.Ssid);
        Assert.False(existingPort.IsRx);
        Assert.True(existingPort.IsTx);
        Assert.Equal("\\", existingPort.SymbolTableCharacter);
        Assert.Equal(">", existingPort.SymbolCodeCharacter);
        Assert.Equal(DynamicBeaconMode.Drive, existingPort.DynamicBeaconMode);
        Assert.False(existingPort.ShowOnMap);
        Assert.Equal(1, configuration.SaveCount);
    }

    [Fact]
    public async Task UpdatePortAsyncRaisesPortsChangedAfterSaving()
    {
        var existingPort = new PortConfig { Id = Guid.NewGuid(), Name = "Port" };
        var configuration = new TestConfigurationService(existingPort);
        var service = CreateService(configuration);
        var changeCount = 0;
        service.PortsChanged += (_, _) => changeCount++;

        existingPort.Name = "Updated";
        await service.UpdatePortAsync(existingPort);

        Assert.Equal(1, changeCount);
    }

    [Fact]
    public async Task UpdatePortAsyncWithUnknownIdDoesNotSaveOrRaiseEvent()
    {
        var configuration = new TestConfigurationService();
        var service = CreateService(configuration);
        var changeCount = 0;
        service.PortsChanged += (_, _) => changeCount++;

        await service.UpdatePortAsync(new PortConfig { Id = Guid.NewGuid() });

        Assert.Equal(0, configuration.SaveCount);
        Assert.Equal(0, changeCount);
    }

    [Fact]
    public async Task AddPortAsyncPersistsPortAndRaisesPortsChanged()
    {
        var configuration = new TestConfigurationService();
        var service = CreateService(configuration);
        var changeCount = 0;
        service.PortsChanged += (_, _) => changeCount++;
        var port = new PortConfig { Id = Guid.NewGuid(), Name = "New port" };

        await service.AddPortAsync(port);

        Assert.Contains(port, configuration.Settings.Ports);
        Assert.Equal(1, configuration.SaveCount);
        Assert.Equal(1, changeCount);
    }

    [Fact]
    public async Task RemovePortAsyncRemovesPortAndRaisesPortsChanged()
    {
        var existingPort = new PortConfig { Id = Guid.NewGuid(), Name = "Port" };
        var configuration = new TestConfigurationService(existingPort);
        var service = CreateService(configuration);
        var changeCount = 0;
        service.PortsChanged += (_, _) => changeCount++;

        await service.RemovePortAsync(existingPort.Id);

        Assert.Empty(configuration.Settings.Ports);
        Assert.Equal(1, configuration.SaveCount);
        Assert.Equal(1, changeCount);
    }

    [Fact]
    public async Task SendPacketAsyncOnEnabledPortWithNullTypeSettingsThrowsNotRunning()
    {
        var port = new PortConfig
        {
            Id = Guid.NewGuid(),
            Name = "Broken port",
            IsEnabled = true,
            TypeSettings = null
        };
        var configuration = new TestConfigurationService(port);
        var service = CreateService(configuration);
        var packet = new PositionPacket
        {
            Source = new Callsign("N0CALL", 1),
            Destination = new Callsign("APRS"),
            Latitude = 0,
            Longitude = 0
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.SendPacketAsync(port.Id, packet));

        Assert.Contains("is not running", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SetPortEnabledAsyncWithNullTypeSettingsLeavesIsEnabledFalse()
    {
        var port = new PortConfig
        {
            Id = Guid.NewGuid(),
            Name = "Broken port",
            IsEnabled = false,
            TypeSettings = null
        };
        var configuration = new TestConfigurationService(port);
        var service = CreateService(configuration);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.SetPortEnabledAsync(port.Id, true));

        Assert.Contains("Failed to start port", ex.Message, StringComparison.Ordinal);
        Assert.False(port.IsEnabled);
        Assert.Equal(2, configuration.SaveCount);
    }


    [Fact]
    public async Task UpdatePortAsync_WhenSessionActive_RestartsModemWithNewSettings()
    {
        var portId = Guid.NewGuid();
        var port = new PortConfig
        {
            Id = portId,
            Name = "KISS",
            IsEnabled = false,
            IsTx = true,
            TypeSettings = new KissSettings
            {
                Transport = new TcpKissTransportSettings { Host = "127.0.0.1", Port = 8001 }
            }
        };
        var configuration = new TestConfigurationService(port);
        var factory = new CountingKissStreamFactory();
        var service = CreateService(configuration, factory);

        await service.SetPortEnabledAsync(portId, true);
        Assert.Equal(1, factory.OpenCount);

        var updated = new PortConfig
        {
            Id = portId,
            Name = "KISS-updated",
            IsEnabled = true,
            IsTx = true,
            TypeSettings = new KissSettings
            {
                Transport = new TcpKissTransportSettings { Host = "127.0.0.1", Port = 8002 }
            }
        };

        await service.UpdatePortAsync(updated);

        Assert.Equal(2, factory.OpenCount);
        Assert.Equal("KISS-updated", port.Name);
        var kiss = Assert.IsType<KissSettings>(port.TypeSettings);
        var tcp = Assert.IsType<TcpKissTransportSettings>(kiss.Transport);
        Assert.Equal(8002, tcp.Port);
    }

    private static PortService CreateService(
        TestConfigurationService configuration,
        IKissStreamFactory? kissStreamFactory = null)
    {
        var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        kissStreamFactory ??= new KissStreamFactory([new TcpKissStreamConnector()]);
        return new PortService(configuration, NullLogger<PortService>.Instance, services, kissStreamFactory);
    }

    private sealed class CountingKissStreamFactory : IKissStreamFactory
    {
        public int OpenCount { get; private set; }

        public IReadOnlyCollection<Type> SupportedTransports { get; } =
            [typeof(TcpKissTransportSettings)];

        public Task<Stream> OpenAsync(KissSettings settings, CancellationToken cancellationToken = default)
        {
            OpenCount++;
            // Never-EOF duplex stub so KissModem.Start does not exit immediately.
            return Task.FromResult<Stream>(new NeverEofMemoryStream());
        }
    }

    private sealed class NeverEofMemoryStream : MemoryStream
    {
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            try
            {
                await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return 0;
            }

            return 0;
        }
    }

    private sealed class TestConfigurationService : IConfigurationService
    {
        public TestConfigurationService(params PortConfig[] ports)
        {
            Settings.Ports.AddRange(ports);
        }

        public AppSettings Settings { get; } = new();

        public int SaveCount { get; private set; }

        public Task SaveSettingsAsync()
        {
            SaveCount++;
            return Task.CompletedTask;
        }
    }
}