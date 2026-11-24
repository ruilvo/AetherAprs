// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Services.Platform;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using System.Threading.Tasks;
using Xunit;

namespace AetherAprs.Tests.Services.Platform;

public sealed class KeepAliveServiceTests
{
    [Fact]
    public async Task AcquireAsync_FirstReason_StartsForegroundService()
    {
        var foreground = Substitute.For<IForegroundService>();
        var service = new KeepAliveService(foreground, NullLogger<KeepAliveService>.Instance);

        await service.AcquireAsync(KeepAliveReason.Ports);

        await foreground.Received(1).StartAsync(ForegroundServiceType.ConnectedDevice);
        await foreground.DidNotReceive().StopAsync();
    }

    [Fact]
    public async Task AcquireAsync_SameReasonTwice_StartsOnlyOnce()
    {
        var foreground = Substitute.For<IForegroundService>();
        var service = new KeepAliveService(foreground, NullLogger<KeepAliveService>.Instance);

        await service.AcquireAsync(KeepAliveReason.Ports);
        await service.AcquireAsync(KeepAliveReason.Ports);

        await foreground.Received(1).StartAsync(Arg.Any<ForegroundServiceType>());
    }

    [Fact]
    public async Task AcquireAsync_DistinctReasons_RefreshesDeclaredTypes()
    {
        var foreground = Substitute.For<IForegroundService>();
        var service = new KeepAliveService(foreground, NullLogger<KeepAliveService>.Instance);

        await service.AcquireAsync(KeepAliveReason.Ports);
        await service.AcquireAsync(KeepAliveReason.LocationTracking);

        await foreground.Received(1).StartAsync(ForegroundServiceType.ConnectedDevice);
        await foreground.Received(1).StartAsync(
            ForegroundServiceType.ConnectedDevice | ForegroundServiceType.Location);
    }

    [Fact]
    public async Task ReleaseAsync_WhileOtherReasonActive_DoesNotStop()
    {
        var foreground = Substitute.For<IForegroundService>();
        var service = new KeepAliveService(foreground, NullLogger<KeepAliveService>.Instance);

        await service.AcquireAsync(KeepAliveReason.Ports);
        await service.AcquireAsync(KeepAliveReason.LocationTracking);

        await service.ReleaseAsync(KeepAliveReason.Ports);

        await foreground.DidNotReceive().StopAsync();
    }

    [Fact]
    public async Task ReleaseAsync_LastReason_StopsForegroundService()
    {
        var foreground = Substitute.For<IForegroundService>();
        var service = new KeepAliveService(foreground, NullLogger<KeepAliveService>.Instance);

        await service.AcquireAsync(KeepAliveReason.Ports);
        await service.AcquireAsync(KeepAliveReason.LocationTracking);

        await service.ReleaseAsync(KeepAliveReason.Ports);
        await service.ReleaseAsync(KeepAliveReason.LocationTracking);

        await foreground.Received(1).StopAsync();
    }

    [Fact]
    public async Task ReleaseAsync_WithoutAcquire_DoesNotStop()
    {
        var foreground = Substitute.For<IForegroundService>();
        var service = new KeepAliveService(foreground, NullLogger<KeepAliveService>.Instance);

        await service.ReleaseAsync(KeepAliveReason.Ports);

        await foreground.DidNotReceive().StopAsync();
        await foreground.DidNotReceive().StartAsync(Arg.Any<ForegroundServiceType>());
    }
}
