// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AetherAprs.Configuration;
using AetherAprs.Data;
using AetherAprs.Extensions;
using AetherAprs.Models;
using AetherAprs.Models.Aprs;
using AetherAprs.Services;
using AetherAprs.Tests.Helpers;
using AetherAprs.Transports.Kiss;
using Microsoft.EntityFrameworkCore;
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
            IsRx = true,
            IsTx = false
        };
        using var db = TempAppDatabase.Create(existingPort);
        var service = CreateService(db);
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
            IsRx = false,
            IsTx = true,
            ShowOnMap = false
        };

        await service.UpdatePortAsync(updatedPort);

        var persisted = Assert.Single(service.Ports);
        var aprsIsSettings = persisted.GetAprsIsSettings();
        Assert.Equal("Updated name", persisted.Name);
        Assert.True(persisted.IsEnabled);
        Assert.NotNull(aprsIsSettings);
        Assert.Equal("new.example", aprsIsSettings.Server);
        Assert.Equal(14501, aprsIsSettings.ServerPort);
        Assert.Equal("new-passcode", aprsIsSettings.Passcode);
        Assert.Equal("m/50", aprsIsSettings.Filter);
        Assert.False(persisted.IsRx);
        Assert.True(persisted.IsTx);
        Assert.False(persisted.ShowOnMap);

        var reloaded = CreateService(db);
        var fromDb = Assert.Single(reloaded.Ports);
        Assert.Equal("Updated name", fromDb.Name);
        Assert.Equal("new.example", fromDb.GetAprsIsSettings()?.Server);
    }

    [Fact]
    public async Task UpdatePortAsyncRaisesPortsChangedAfterSaving()
    {
        var existingPort = new PortConfig { Id = Guid.NewGuid(), Name = "Port" };
        using var db = TempAppDatabase.Create(existingPort);
        var service = CreateService(db);
        var changeCount = 0;
        service.PortsChanged += (_, _) => changeCount++;

        existingPort.Name = "Updated";
        await service.UpdatePortAsync(existingPort);

        Assert.Equal(1, changeCount);
    }

    [Fact]
    public async Task UpdatePortAsyncWithUnknownIdDoesNotSaveOrRaiseEvent()
    {
        using var db = TempAppDatabase.Create();
        var service = CreateService(db);
        var changeCount = 0;
        service.PortsChanged += (_, _) => changeCount++;

        await service.UpdatePortAsync(new PortConfig { Id = Guid.NewGuid() });

        Assert.Empty(service.Ports);
        Assert.Equal(0, changeCount);
        using var context = db.CreateContext();
        Assert.Empty(context.Ports.AsNoTracking());
    }

    [Fact]
    public async Task AddPortAsyncPersistsPortAndRaisesPortsChanged()
    {
        using var db = TempAppDatabase.Create();
        var service = CreateService(db);
        var changeCount = 0;
        service.PortsChanged += (_, _) => changeCount++;
        var port = new PortConfig { Id = Guid.NewGuid(), Name = "New port" };

        await service.AddPortAsync(port);

        Assert.Contains(port, service.Ports);
        Assert.Equal(1, changeCount);
        var reloaded = CreateService(db);
        Assert.Equal("New port", Assert.Single(reloaded.Ports).Name);
    }

    [Fact]
    public async Task RemovePortAsyncRemovesPortAndRaisesPortsChanged()
    {
        var existingPort = new PortConfig { Id = Guid.NewGuid(), Name = "Port" };
        using var db = TempAppDatabase.Create(existingPort);
        var service = CreateService(db);
        var changeCount = 0;
        service.PortsChanged += (_, _) => changeCount++;

        await service.RemovePortAsync(existingPort.Id);

        Assert.Empty(service.Ports);
        Assert.Equal(1, changeCount);
        var reloaded = CreateService(db);
        Assert.Empty(reloaded.Ports);
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
        using var db = TempAppDatabase.Create(port);
        var service = CreateService(db);
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
        using var db = TempAppDatabase.Create(port);
        var service = CreateService(db);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.SetPortEnabledAsync(port.Id, true));

        Assert.Contains("Failed to start port", ex.Message, StringComparison.Ordinal);
        Assert.False(Assert.Single(service.Ports).IsEnabled);
        using var context = db.CreateContext();
        Assert.False(Assert.Single(context.Ports.AsNoTracking()).IsEnabled);
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
        using var db = TempAppDatabase.Create(port);
        var factory = new CountingKissStreamFactory();
        var service = CreateService(db, factory);

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
        var persisted = Assert.Single(service.Ports);
        Assert.Equal("KISS-updated", persisted.Name);
        var kiss = Assert.IsType<KissSettings>(persisted.TypeSettings);
        var tcp = Assert.IsType<TcpKissTransportSettings>(kiss.Transport);
        Assert.Equal(8002, tcp.Port);

        await service.StopAllPortsAsync();
    }

    private static PortService CreateService(
        TempAppDatabase db,
        IKissStreamFactory? kissStreamFactory = null)
    {
        var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        kissStreamFactory ??= new KissStreamFactory([new TcpKissStreamConnector()]);
        return new PortService(
            db.Factory,
            new TestConfigurationService(),
            NullLogger<PortService>.Instance,
            services,
            kissStreamFactory);
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
        public AppSettings Settings { get; } = new();

        public Task SaveSettingsAsync() => Task.CompletedTask;
    }
}
