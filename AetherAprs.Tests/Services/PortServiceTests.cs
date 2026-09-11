// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Threading.Tasks;
using AetherAprs.Configuration;
using AetherAprs.Models;
using AetherAprs.Models.Aprs;
using AetherAprs.Services;
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
            Type = PortType.AprsIs,
            Name = "Old name",
            Server = "old.example",
            ServerPort = 14580,
            Passcode = "old-passcode",
            Filter = "m/10",
            Ssid = 1,
            IsRx = true,
            IsTx = false
        };
        var configuration = new TestConfigurationService(existingPort);
        var service = CreateService(configuration);
        var updatedPort = new PortConfig
        {
            Id = existingPort.Id,
            Type = PortType.Kiss,
            Name = "Updated name",
            IsEnabled = true,
            Server = "new.example",
            ServerPort = 14501,
            Passcode = "new-passcode",
            Filter = "m/50",
            Ssid = 2,
            IsRx = false,
            IsTx = true,
            SymbolTableCharacter = "\\",
            SymbolCodeCharacter = ">",
            DynamicBeaconMode = DynamicBeaconMode.Drive
        };

        await service.UpdatePortAsync(updatedPort);

        Assert.Equal("Updated name", existingPort.Name);
        Assert.Equal(PortType.Kiss, existingPort.Type);
        Assert.True(existingPort.IsEnabled);
        Assert.Equal("new.example", existingPort.Server);
        Assert.Equal(14501, existingPort.ServerPort);
        Assert.Equal("new-passcode", existingPort.Passcode);
        Assert.Equal("m/50", existingPort.Filter);
        Assert.Equal(2, existingPort.Ssid);
        Assert.False(existingPort.IsRx);
        Assert.True(existingPort.IsTx);
        Assert.Equal("\\", existingPort.SymbolTableCharacter);
        Assert.Equal(">", existingPort.SymbolCodeCharacter);
        Assert.Equal(DynamicBeaconMode.Drive, existingPort.DynamicBeaconMode);
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

    private static PortService CreateService(TestConfigurationService configuration)
    {
        var services = new ServiceCollection().BuildServiceProvider();
        return new PortService(configuration, NullLogger<PortService>.Instance, services);
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
