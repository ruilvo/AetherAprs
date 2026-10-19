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
        var provider = ServiceProviderFactory.CreateServiceProvider(_ => { }, _ => { });

        Assert.IsType<AppDataDirProviderService>(provider.GetRequiredService<IAppDataDirProviderService>());
        Assert.IsType<NoOpLocationService>(provider.GetRequiredService<ILocationService>());
        Assert.IsType<NoOpPermissionService>(provider.GetRequiredService<IPermissionService>());
        Assert.IsType<NoOpForegroundService>(provider.GetRequiredService<IForegroundService>());
    }

    [Fact]
    public void CreateServiceProvider_WithPlatformServices_PlatformOverridesDefaults()
    {
        var provider = ServiceProviderFactory.CreateServiceProvider(
            services => services.AddSingleton<ILocationService, TestLocationService>(),
            _ => { });

        Assert.IsType<TestLocationService>(provider.GetRequiredService<ILocationService>());
    }

    private sealed class TestLocationService : ILocationService
    {
        public Task<LocationData> GetCurrentLocationAsync(CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Not available in test.");

        public bool IsLocationAvailable() => false;

        public Task<bool> RequestLocationPermissionAsync() => Task.FromResult(false);
    }
}
