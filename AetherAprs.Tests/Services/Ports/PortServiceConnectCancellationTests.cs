// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AetherAprs.Configuration;
using AetherAprs.Configuration.Settings;
using AetherAprs.Models.Aprs.Packets;
using AetherAprs.Services.Configuration;
using AetherAprs.Services.Contracts;
using AetherAprs.Services.Packets;
using AetherAprs.Services.Platform;
using AetherAprs.Services.Ports;
using AetherAprs.Tests.Helpers;
using AetherAprs.Transports.Kiss;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace AetherAprs.Tests.Services.Ports;

/// <summary>
/// Regression tests for audit P-18: the session start path must forward the
/// caller's cancellation token through <see cref="IKissStreamFactory"/> to the
/// Bluetooth connector, so a stuck BLE/RFCOMM connect can be aborted.
/// </summary>
public sealed class PortServiceConnectCancellationTests
{
    [Fact]
    public async Task StartSessionAsync_ForwardsCancellationToBluetoothConnectors()
    {
        var classicPortId = Guid.NewGuid();
        var lePortId = Guid.NewGuid();
        var classicConnector = new BlockingConnector(typeof(BluetoothClassicKissTransportSettings));
        var leConnector = new BlockingConnector(typeof(BluetoothLeKissTransportSettings));
        var factory = new KissStreamFactory([classicConnector, leConnector]);
        using var db = TempAppDatabase.Create(
            CreatePort(classicPortId, new BluetoothClassicKissTransportSettings
            {
                DeviceAddress = "00:11:22:33:44:55"
            }),
            CreatePort(lePortId, new BluetoothLeKissTransportSettings
            {
                DeviceAddress = "AA:BB:CC:DD:EE:FF"
            }));
        await using var service = CreateService(db, factory);
        await service.InitializeAsync(TestContext.Current.CancellationToken);

        await AssertCancellationIsForwardedAsync(service, classicPortId, classicConnector);
        await AssertCancellationIsForwardedAsync(service, lePortId, leConnector);
    }

    private static async Task AssertCancellationIsForwardedAsync(
        PortService service,
        Guid portId,
        BlockingConnector connector)
    {
        using var cts = new CancellationTokenSource();
        var startTask = service.StartSessionAsync(portId, cts.Token);

        await connector.Entered.Task.WaitAsync(
            TestTimeouts.TestTimeout,
            TestContext.Current.CancellationToken);
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            startTask.WaitAsync(TestTimeouts.TestTimeout, TestContext.Current.CancellationToken));

        Assert.True(connector.CapturedToken.CanBeCanceled);
        Assert.True(connector.CapturedToken.IsCancellationRequested);
        Assert.True(connector.ObservedCancellation);
    }

    private static PortConfig CreatePort(Guid portId, IKissTransportSettings transport) => new()
    {
        Id = portId,
        Name = $"Bluetooth {portId:N}",
        IsEnabled = false,
        IsRx = true,
        IsTx = true,
        TypeSettings = new KissSettings
        {
            Transport = transport
        }
    };

    private static PortService CreateService(TempAppDatabase db, IKissStreamFactory kissStreamFactory)
    {
        var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        var digipeater = Substitute.For<IDigipeaterService>();
        digipeater.GetDigipeatTargets(
            Arg.Any<AprsPacket>(),
            Arg.Any<Guid>(),
            Arg.Any<bool>(),
            Arg.Any<IReadOnlyList<PortInfo>>())
            .Returns([]);

        return new PortService(
            db.Factory,
            new TestConfigurationService(),
            services.GetRequiredService<ILoggerFactory>(),
            NullLogger<PortService>.Instance,
            kissStreamFactory,
            Substitute.For<IPacketStorageService>(),
            Substitute.For<IKeepAliveService>(),
            digipeater,
            Substitute.For<IAudioStreamService>(),
            Substitute.For<IAudioPlaybackService>(),
            Substitute.For<IPttService>(),
            new NullSecretProtector());
    }

    private sealed class BlockingConnector(Type settingsType) : IKissStreamConnector
    {
        public Type SettingsType { get; } = settingsType;

        public bool IsSupported => true;

        public TaskCompletionSource Entered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public CancellationToken CapturedToken { get; private set; }

        public bool ObservedCancellation { get; private set; }

        public async Task<Stream> ConnectAsync(
            IKissTransportSettings settings,
            CancellationToken cancellationToken = default)
        {
            CapturedToken = cancellationToken;
            Entered.TrySetResult();

            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                ObservedCancellation = true;
                throw;
            }

            throw new InvalidOperationException("The blocking connect completed unexpectedly.");
        }
    }

    private sealed class TestConfigurationService : IConfigurationService
    {
#pragma warning disable CS0067 // Event is never used in this test double
        public event EventHandler? SettingsChanged;
#pragma warning restore CS0067

        public AppSettings Settings { get; } = new();

        public Task SaveSettingsAsync() => Task.CompletedTask;

        public Task UpdateSettingsAsync(Action<AppSettings> updateAction)
        {
            updateAction(Settings);
            return Task.CompletedTask;
        }

        public AppSettings GetSettingsSnapshot() => Settings;
    }
}
