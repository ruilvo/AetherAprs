// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AetherAprs.Configuration;
using AetherAprs.Models.Aprs.Packets;
using AetherAprs.Services.Contracts;
using AetherAprs.Services.Packets;
using AetherAprs.Services.Platform;
using AetherAprs.Services.Ports;
using AetherAprs.Tests.Helpers;
using AetherAprs.Transports.Kiss;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace AetherAprs.Tests.Services.Ports;

/// <summary>
/// Regression tests: the session start path must forward the
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

        // Cancellation is reported as a structured result rather than an exception,
        // so supervision can distinguish it from a rejected transport, but the
        // original OperationCanceledException must still be attached.
        var result = await startTask.WaitAsync(TestTimeouts.TestTimeout, TestContext.Current.CancellationToken);

        Assert.Equal(PortSessionStartStatus.Cancelled, result.Status);
        Assert.IsAssignableFrom<OperationCanceledException>(result.Error);
        Assert.False(result.IsRunning);
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
        var digipeater = Substitute.For<IDigipeaterService>();
        digipeater.GetDigipeatTargets(
            Arg.Any<AprsPacket>(),
            Arg.Any<Guid>(),
            Arg.Any<bool>(),
            Arg.Any<IReadOnlyList<PortInfo>>())
            .Returns([]);

        return TestPortService.Create(
            db,
            TestSessionManager.Create(TestModemFactory.Create(kissStreamFactory: kissStreamFactory)),
            new TestPortService.PortServiceCollaborators
            {
                PacketStorageService = Substitute.For<IPacketStorageService>(),
                DigipeaterService = digipeater,
                KeepAliveService = Substitute.For<IKeepAliveService>()
            });
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

}
