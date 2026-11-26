// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Threading;
using System.Threading.Tasks;
using AetherAprs.Configuration;
using AetherAprs.Services.Platform;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace AetherAprs.Tests.Services.Platform;

public sealed class PortPermissionPrompterTests
{
    [Fact]
    public async Task RequestForPortAsync_BluetoothPort_RequestsNotificationAndBluetoothOnly()
    {
        IPermissionService permissions = AllowAll();
        PortPermissionPrompter prompter = CreatePrompter(permissions);

        await prompter.RequestForPortAsync(
            CreateKissPort(new BluetoothLeKissTransportSettings()),
            TestContext.Current.CancellationToken);

        await permissions.Received(1).RequestAsync(PermissionKind.Notifications, Arg.Any<CancellationToken>());
        await permissions.Received(1).RequestAsync(PermissionKind.Bluetooth, Arg.Any<CancellationToken>());
        await permissions.DidNotReceive().RequestAsync(PermissionKind.Audio, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RequestForPortAsync_AfskPort_RequestsNotificationAndAudioOnly()
    {
        IPermissionService permissions = AllowAll();
        PortPermissionPrompter prompter = CreatePrompter(permissions);

        await prompter.RequestForPortAsync(
            new PortConfig
            {
                Id = Guid.NewGuid(),
                Name = "AFSK",
                IsEnabled = true,
                TypeSettings = new AfskSettings()
            },
            TestContext.Current.CancellationToken);

        await permissions.Received(1).RequestAsync(PermissionKind.Notifications, Arg.Any<CancellationToken>());
        await permissions.Received(1).RequestAsync(PermissionKind.Audio, Arg.Any<CancellationToken>());
        await permissions.DidNotReceive().RequestAsync(PermissionKind.Bluetooth, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RequestForPortAsync_NetworkPort_RequestsOnlyNotification()
    {
        IPermissionService permissions = AllowAll();
        PortPermissionPrompter prompter = CreatePrompter(permissions);

        await prompter.RequestForPortAsync(
            CreateKissPort(new TcpKissTransportSettings()),
            TestContext.Current.CancellationToken);

        await permissions.Received(1).RequestAsync(PermissionKind.Notifications, Arg.Any<CancellationToken>());
        await permissions.DidNotReceive().RequestAsync(PermissionKind.Bluetooth, Arg.Any<CancellationToken>());
        await permissions.DidNotReceive().RequestAsync(PermissionKind.Audio, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RequestForPortAsync_WhenADenialIsPermanent_ReportsItForTheOperator()
    {
        IPermissionService permissions = AllowAll();
        permissions.RequestAsync(PermissionKind.Bluetooth, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(PermissionRequestResult.PermanentlyDenied(PermissionKind.Bluetooth)));
        PermissionAttentionService attention = new();
        PortPermissionPrompter prompter = CreatePrompter(permissions, attention);

        await prompter.RequestForPortAsync(
            CreateKissPort(new BluetoothClassicKissTransportSettings()),
            TestContext.Current.CancellationToken);

        PermissionAttention reported = Assert.Single(attention.Outstanding);
        Assert.Equal(PermissionKind.Bluetooth, reported.Kind);
        Assert.True(reported.RequiresApplicationSettings);
    }

    [Fact]
    public async Task RequestForPortAsync_WhenTheOperatorGrantsAfterADenial_ClearsTheAttention()
    {
        IPermissionService permissions = AllowAll();
        permissions.RequestAsync(PermissionKind.Audio, Arg.Any<CancellationToken>())
            .Returns(
                Task.FromResult(PermissionRequestResult.Denied(PermissionKind.Audio)),
                Task.FromResult(PermissionRequestResult.Granted(PermissionKind.Audio)));
        PermissionAttentionService attention = new();
        PortPermissionPrompter prompter = CreatePrompter(permissions, attention);
        PortConfig port = new()
        {
            Id = Guid.NewGuid(),
            Name = "AFSK",
            IsEnabled = true,
            TypeSettings = new AfskSettings()
        };

        await prompter.RequestForPortAsync(port, TestContext.Current.CancellationToken);
        Assert.Single(attention.Outstanding);

        await prompter.RequestForPortAsync(port, TestContext.Current.CancellationToken);
        Assert.Empty(attention.Outstanding);
    }

    private static PortPermissionPrompter CreatePrompter(
        IPermissionService permissions,
        IPermissionAttentionReporter? attention = null) =>
        new(permissions, NullLogger<PortPermissionPrompter>.Instance, attention);

    private static IPermissionService AllowAll()
    {
        IPermissionService permissions = Substitute.For<IPermissionService>();
        permissions.RequestAsync(Arg.Any<PermissionKind>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(PermissionRequestResult.Granted(call.Arg<PermissionKind>())));
        return permissions;
    }

    private static PortConfig CreateKissPort(IKissTransportSettings transport) =>
        new()
        {
            Id = Guid.NewGuid(),
            Name = "KISS",
            IsEnabled = true,
            TypeSettings = new KissSettings { Transport = transport }
        };
}
