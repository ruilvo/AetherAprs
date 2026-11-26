// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Factories;
using AetherAprs.Services.Platform;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using AetherAprs.Models;

namespace AetherAprs.Tests.Factories;

public class ServiceProviderFactoryTests
{
    [Fact]
    public void CreateServiceProvider_WithoutPlatformServices_ResolvesCoreDefaults()
    {
        var bootstrap = new AppDataDirProviderService();
        var provider = ServiceProviderFactory.CreateServiceProvider(bootstrap, _ => { }, _ => { });

        Assert.Same(bootstrap, provider.GetRequiredService<IAppDataDirProviderService>());
        Assert.IsType<NoOpLocationService>(provider.GetRequiredService<ILocationService>());
        Assert.IsType<NoOpPermissionService>(provider.GetRequiredService<IPermissionService>());
        Assert.IsType<NoOpForegroundService>(provider.GetRequiredService<IForegroundService>());
        Assert.IsType<KeepAliveService>(provider.GetRequiredService<IKeepAliveService>());
        Assert.IsType<NoOpNotificationService>(provider.GetRequiredService<INotificationService>());
    }

    [Fact]
    public void CreateServiceProvider_WithPlatformServices_PlatformOverridesDefaults()
    {
        var provider = ServiceProviderFactory.CreateServiceProvider(
            new AppDataDirProviderService(),
            _ => { },
            overrides => overrides.Replace<ILocationService, TestLocationService>());

        Assert.IsType<TestLocationService>(provider.GetRequiredService<ILocationService>());
    }

    [Fact]
    public void CreateServiceProvider_ReplacingAnUnregisteredService_IsRejected()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            ServiceProviderFactory.CreateServiceProvider(
                new AppDataDirProviderService(),
                _ => { },
                overrides => overrides.Replace<IUnregisteredService, UnregisteredService>()));

        Assert.Contains(nameof(IUnregisteredService), exception.Message, StringComparison.Ordinal);
    }

    private interface IUnregisteredService
    {
    }

    private sealed class UnregisteredService : IUnregisteredService
    {
    }

    [Fact]
    public void CreateServiceProvider_WithoutABootstrap_IsRejected()
    {
        Assert.Throws<ArgumentNullException>(() =>
            ServiceProviderFactory.CreateServiceProvider(null!, _ => { }, _ => { }));
    }

    private sealed class TestLocationService : ILocationService
    {
        public Task<LocationData> GetCurrentLocationAsync(CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Not available in test.");

        public bool IsLocationAvailable() => false;

        public Task<bool> RequestLocationPermissionAsync() => Task.FromResult(false);
    }
}
