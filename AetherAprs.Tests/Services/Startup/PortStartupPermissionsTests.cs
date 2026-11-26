// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Threading;
using System.Threading.Tasks;
using AetherAprs.Configuration;
using AetherAprs.Services.Platform;
using AetherAprs.Services.Startup;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace AetherAprs.Tests.Services.Startup;

public sealed class PortStartupPermissionsTests
{
    [Fact]
    public void RequiresNotification_IsDecidedByWhetherAnyPortIsEnabled()
    {
        Assert.True(PortStartupPermissions.RequiresNotification([CreateKissPort(enabled: true)]));
        Assert.False(PortStartupPermissions.RequiresNotification([CreateKissPort(enabled: false)]));
        Assert.False(PortStartupPermissions.RequiresNotification([]));
    }

    [Fact]
    public void RequiresBluetooth_RecognizesBothBluetoothTransports()
    {
        var classic = CreateKissPort(enabled: true, new BluetoothClassicKissTransportSettings());
        var lowEnergy = CreateKissPort(enabled: true, new BluetoothLeKissTransportSettings());
        var tcp = CreateKissPort(enabled: true, new TcpKissTransportSettings());
        var disabledBluetooth = CreateKissPort(enabled: false, new BluetoothLeKissTransportSettings());

        Assert.True(PortStartupPermissions.RequiresBluetooth([classic]));
        Assert.True(PortStartupPermissions.RequiresBluetooth([lowEnergy]));
        Assert.False(PortStartupPermissions.RequiresBluetooth([tcp]));
        Assert.False(PortStartupPermissions.RequiresBluetooth([disabledBluetooth]));
    }

    [Fact]
    public void RequiresAudio_IsDecidedByWhetherAnAfskPortIsEnabled()
    {
        Assert.True(PortStartupPermissions.RequiresAudio([CreateAfskPort(enabled: true)]));
        Assert.False(PortStartupPermissions.RequiresAudio([CreateAfskPort(enabled: false)]));
        Assert.False(PortStartupPermissions.RequiresAudio([CreateKissPort(enabled: true)]));
    }

    [Fact]
    public async Task RequestAsync_WithoutEnabledPorts_RequestsNothing()
    {
        var permissions = Substitute.For<IPermissionService>();

        await PortStartupPermissions.RequestAsync(
            [CreateAfskPort(enabled: false), CreateKissPort(enabled: false, new BluetoothLeKissTransportSettings())],
            permissions,
            NullLogger.Instance,
            TestContext.Current.CancellationToken);

        await permissions.DidNotReceive().RequestAsync(Arg.Any<PermissionKind>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RequestAsync_RequestsOnlyWhatTheEnabledPortsNeed()
    {
        var permissions = AllowAll();
        var ports = new[]
        {
            CreateKissPort(enabled: true, new BluetoothLeKissTransportSettings()),
            CreateAfskPort(enabled: false)
        };

        await PortStartupPermissions.RequestAsync(
            ports,
            permissions,
            NullLogger.Instance,
            TestContext.Current.CancellationToken);

        await permissions.Received(1).RequestAsync(PermissionKind.Notifications, Arg.Any<CancellationToken>());
        await permissions.Received(1).RequestAsync(PermissionKind.Bluetooth, Arg.Any<CancellationToken>());
        await permissions.DidNotReceive().RequestAsync(PermissionKind.Audio, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(PermissionOutcome.Denied)]
    [InlineData(PermissionOutcome.PermanentlyDenied)]
    [InlineData(PermissionOutcome.Unavailable)]
    public async Task RequestAsync_AnUnmetPermission_DoesNotSkipTheNextRequest(PermissionOutcome outcome)
    {
        var permissions = AllowAll();
        permissions.RequestAsync(PermissionKind.Bluetooth, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new PermissionRequestResult(PermissionKind.Bluetooth, outcome)));
        var ports = new[]
        {
            CreateKissPort(enabled: true, new BluetoothClassicKissTransportSettings()),
            CreateAfskPort(enabled: true)
        };

        await PortStartupPermissions.RequestAsync(
            ports,
            permissions,
            NullLogger.Instance,
            TestContext.Current.CancellationToken);

        await permissions.Received(1).RequestAsync(PermissionKind.Bluetooth, Arg.Any<CancellationToken>());
        await permissions.Received(1).RequestAsync(PermissionKind.Audio, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RequestAsync_WhenAPermissionIsBlocked_ReportsItForTheOperator()
    {
        var permissions = AllowAll();
        permissions.RequestAsync(PermissionKind.Bluetooth, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(PermissionRequestResult.PermanentlyDenied(PermissionKind.Bluetooth)));
        var attention = new PermissionAttentionService();

        await PortStartupPermissions.RequestAsync(
            [CreateKissPort(enabled: true, new BluetoothLeKissTransportSettings())],
            permissions,
            NullLogger.Instance,
            TestContext.Current.CancellationToken,
            attention);

        var reported = Assert.Single(attention.Outstanding);
        Assert.Equal(PermissionKind.Bluetooth, reported.Kind);
        Assert.True(reported.RequiresApplicationSettings);
    }

    private static IPermissionService AllowAll()
    {
        var permissions = Substitute.For<IPermissionService>();
        permissions.RequestAsync(Arg.Any<PermissionKind>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(PermissionRequestResult.Granted(call.Arg<PermissionKind>())));
        return permissions;
    }

    private static PortConfig CreateKissPort(
        bool enabled,
        IKissTransportSettings? transport = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            Name = "KISS",
            IsEnabled = enabled,
            TypeSettings = new KissSettings { Transport = transport ?? new TcpKissTransportSettings() }
        };

    private static PortConfig CreateAfskPort(bool enabled) =>
        new()
        {
            Id = Guid.NewGuid(),
            Name = "AFSK",
            IsEnabled = enabled,
            TypeSettings = new AfskSettings()
        };
}
