// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Configuration;
using AetherAprs.Services.Ports;
using AetherAprs.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;
using Xunit;

namespace AetherAprs.Tests.Services.Ports;

public sealed class PortConfigurationServiceTests
{
    [Fact]
    public async Task SetEnabledAsync_WhenPersistFails_KeepsThePublishedValue()
    {
        var port = new PortConfig { Id = Guid.NewGuid(), Name = "RF", IsEnabled = false };
        using var db = TempAppDatabase.Create(port);
        var configuration = StoreFactory.CreatePortConfigurationService(db);
        await configuration.InitializeAsync(TestContext.Current.CancellationToken);

        await using (var context = db.CreateContext())
        {
            await context.Database.ExecuteSqlRawAsync(
                "DROP TABLE Ports;",
                TestContext.Current.CancellationToken);
        }

        await Assert.ThrowsAnyAsync<Exception>(() =>
            configuration.SetEnabledAsync(port.Id, true, TestContext.Current.CancellationToken));

        var stored = configuration.Find(port.Id);
        Assert.NotNull(stored);
        Assert.False(stored!.IsEnabled);
    }

    [Fact]
    public async Task GetPorts_ReturnsIndependentCopies()
    {
        var port = new PortConfig { Id = Guid.NewGuid(), Name = "RF" };
        using var db = TempAppDatabase.Create(port);
        var configuration = StoreFactory.CreatePortConfigurationService(db);
        await configuration.InitializeAsync(TestContext.Current.CancellationToken);

        var published = Assert.Single(configuration.GetPorts());
        published.Name = "mutated";
        published.IsEnabled = true;

        var stored = configuration.Find(port.Id);
        Assert.NotNull(stored);
        Assert.Equal("RF", stored!.Name);
        Assert.False(stored.IsEnabled);
    }

    [Fact]
    public async Task Mutations_RaisePortsChangedOnlyWhenTheStateChanged()
    {
        var port = new PortConfig { Id = Guid.NewGuid(), Name = "RF", ShowOnMap = true };
        using var db = TempAppDatabase.Create(port);
        var configuration = StoreFactory.CreatePortConfigurationService(db);
        await configuration.InitializeAsync(TestContext.Current.CancellationToken);
        var changes = 0;
        configuration.PortsChanged += (_, _) => changes++;

        await configuration.SetShowOnMapAsync(port.Id, true, TestContext.Current.CancellationToken);
        Assert.Equal(0, changes);

        await configuration.SetShowOnMapAsync(port.Id, false, TestContext.Current.CancellationToken);
        Assert.Equal(1, changes);
        var stored = configuration.Find(port.Id);
        Assert.NotNull(stored);
        Assert.False(stored!.ShowOnMap);
    }

    [Fact]
    public async Task UpdateAsync_ForAnUnconfiguredPort_ReportsNoChange()
    {
        using var db = TempAppDatabase.Create();
        var configuration = StoreFactory.CreatePortConfigurationService(db);
        await configuration.InitializeAsync(TestContext.Current.CancellationToken);

        var updated = await configuration.UpdateAsync(
            new PortConfig { Id = Guid.NewGuid(), Name = "Missing" },
            TestContext.Current.CancellationToken);

        Assert.False(updated);
        Assert.Empty(configuration.GetPorts());
    }
}
