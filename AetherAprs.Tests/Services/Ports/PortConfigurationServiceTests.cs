// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Configuration;
using AetherAprs.Data;
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
        PortConfig port = new() { Id = Guid.NewGuid(), Name = "RF", IsEnabled = false };
        using TempAppDatabase db = TempAppDatabase.Create(port);
        IPortConfigurationService configuration = StoreFactory.CreatePortConfigurationService(db);
        await configuration.InitializeAsync(TestContext.Current.CancellationToken);

        await using (AppDbContext context = db.CreateContext())
        {
            await context.Database.ExecuteSqlRawAsync(
                "DROP TABLE Ports;",
                TestContext.Current.CancellationToken);
        }

        await Assert.ThrowsAnyAsync<Exception>(() =>
            configuration.SetEnabledAsync(port.Id, true, TestContext.Current.CancellationToken));

        PortConfig? stored = configuration.Find(port.Id);
        Assert.NotNull(stored);
        Assert.False(stored!.IsEnabled);
    }

    [Fact]
    public async Task GetPorts_ReturnsIndependentCopies()
    {
        PortConfig port = new() { Id = Guid.NewGuid(), Name = "RF" };
        using TempAppDatabase db = TempAppDatabase.Create(port);
        IPortConfigurationService configuration = StoreFactory.CreatePortConfigurationService(db);
        await configuration.InitializeAsync(TestContext.Current.CancellationToken);

        PortConfig published = Assert.Single(configuration.GetPorts());
        published.Name = "mutated";
        published.IsEnabled = true;

        PortConfig? stored = configuration.Find(port.Id);
        Assert.NotNull(stored);
        Assert.Equal("RF", stored!.Name);
        Assert.False(stored.IsEnabled);
    }

    [Fact]
    public async Task Mutations_RaisePortsChangedOnlyWhenTheStateChanged()
    {
        PortConfig port = new() { Id = Guid.NewGuid(), Name = "RF", ShowOnMap = true };
        using TempAppDatabase db = TempAppDatabase.Create(port);
        IPortConfigurationService configuration = StoreFactory.CreatePortConfigurationService(db);
        await configuration.InitializeAsync(TestContext.Current.CancellationToken);
        int changes = 0;
        configuration.PortsChanged += (_, _) => changes++;

        await configuration.SetShowOnMapAsync(port.Id, true, TestContext.Current.CancellationToken);
        Assert.Equal(0, changes);

        await configuration.SetShowOnMapAsync(port.Id, false, TestContext.Current.CancellationToken);
        Assert.Equal(1, changes);
        PortConfig? stored = configuration.Find(port.Id);
        Assert.NotNull(stored);
        Assert.False(stored!.ShowOnMap);
    }

    [Fact]
    public async Task UpdateAsync_ForAnUnconfiguredPort_ReportsNoChange()
    {
        using TempAppDatabase db = TempAppDatabase.Create();
        IPortConfigurationService configuration = StoreFactory.CreatePortConfigurationService(db);
        await configuration.InitializeAsync(TestContext.Current.CancellationToken);

        bool updated = await configuration.UpdateAsync(
            new PortConfig { Id = Guid.NewGuid(), Name = "Missing" },
            TestContext.Current.CancellationToken);

        Assert.False(updated);
        Assert.Empty(configuration.GetPorts());
    }
}
