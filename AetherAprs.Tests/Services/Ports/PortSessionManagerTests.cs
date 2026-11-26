// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Configuration;
using AetherAprs.Models.Aprs;
using AetherAprs.Models.Aprs.Packets;
using AetherAprs.Services.Contracts;
using AetherAprs.Services.Ports;
using AetherAprs.Tests.Helpers;
using Geo;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace AetherAprs.Tests.Services.Ports;

/// <summary>
/// The session manager is the only owner of running modems: it serializes starts
/// and stops per port, leases the modem for the duration of a send, and never lets
/// a caller touch a modem it has disposed.
/// </summary>
public sealed class PortSessionManagerTests
{
    private static readonly TimeSpan SendDrainDeadline = TimeSpan.FromMilliseconds(150);

    [Fact]
    public async Task ConcurrentStarts_CreateOneModem()
    {
        ModemFactory factory = new(_ => new ScriptedModem());
        await using PortSessionManager manager = TestSessionManager.Create(factory);
        PortConfig port = CreateKissPort();

        PortSessionStartResult[] results = await Task.WhenAll(
            manager.StartAsync(port, TestContext.Current.CancellationToken),
            manager.StartAsync(port, TestContext.Current.CancellationToken));

        Assert.Equal(1, factory.CreateCount);
        Assert.Single(results, result => result.Status == PortSessionStartStatus.Started);
        Assert.Single(results, result => result.Status == PortSessionStartStatus.AlreadyRunning);
        Assert.True(manager.IsRunning(port.Id));
    }

    [Fact]
    public async Task ConcurrentSendAndStop_NeverSendsThroughADisposedModem()
    {
        ScriptedModem modem = new() { BlockSends = true };
        await using PortSessionManager manager = TestSessionManager.Create(new ModemFactory(_ => modem));
        PortConfig port = CreateKissPort();
        await manager.StartAsync(port, TestContext.Current.CancellationToken);

        Task<PortSessionSendStatus> sendTask = manager.SendAsync(port.Id, CreatePacket(), TestContext.Current.CancellationToken);
        await modem.SendEntered.WaitAsync(
            TestTimeouts.TestTimeout,
            TestContext.Current.CancellationToken);

        // The stop must not dispose the modem while a send is inside it.
        Task<PortSessionStopResult> stopTask = manager.StopAsync(port.Id, TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        Assert.Equal(0, modem.DisposeCount);

        modem.ReleaseSend();

        Assert.Equal(
            PortSessionSendStatus.Sent,
            await sendTask.WaitAsync(TestTimeouts.TestTimeout, TestContext.Current.CancellationToken));

        PortSessionStopResult stop = await stopTask.WaitAsync(TestTimeouts.TestTimeout, TestContext.Current.CancellationToken);

        Assert.Equal(PortSessionStopStatus.Stopped, stop.Status);
        Assert.False(stop.SendDrainTimedOut);
        Assert.Equal(1, modem.DisposeCount);
        Assert.False(modem.LastSendObservedDisposedModem);
    }

    [Fact]
    public async Task Stop_WaitsForAnInFlightSendUntilTheDeadline()
    {
        ScriptedModem modem = new() { BlockSends = true };
        await using PortSessionManager manager = TestSessionManager.Create(new ModemFactory(_ => modem));
        PortConfig port = CreateKissPort();
        await manager.StartAsync(port, TestContext.Current.CancellationToken);

        Task<PortSessionSendStatus> sendTask = manager.SendAsync(port.Id, CreatePacket(), TestContext.Current.CancellationToken);
        await modem.SendEntered.WaitAsync(
            TestTimeouts.TestTimeout,
            TestContext.Current.CancellationToken);

        Stopwatch stopwatch = Stopwatch.StartNew();
        PortSessionStopResult stop = await manager
            .StopAsync(port.Id, SendDrainDeadline, TestContext.Current.CancellationToken)
            .WaitAsync(TestTimeouts.TestTimeout, TestContext.Current.CancellationToken);
        stopwatch.Stop();

        Assert.Equal(PortSessionStopStatus.Stopped, stop.Status);
        Assert.True(stop.SendDrainTimedOut);
        Assert.True(
            stopwatch.Elapsed >= TimeSpan.FromMilliseconds(100),
            $"The stop returned after {stopwatch.Elapsed} instead of waiting for the drain deadline.");
        Assert.Equal(1, modem.DisposeCount);

        // The abandoned send still finishes; it must not throw into the test.
        modem.ReleaseSend();
        Assert.Equal(
            PortSessionSendStatus.Sent,
            await sendTask.WaitAsync(TestTimeouts.TestTimeout, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task FailedStop_PermitsALaterStart()
    {
        ScriptedModem modem = new() { ThrowOnStop = true };
        await using PortSessionManager manager = TestSessionManager.Create(new ModemFactory(_ => modem));
        PortConfig port = CreateKissPort();
        await manager.StartAsync(port, TestContext.Current.CancellationToken);

        PortSessionStopResult stop = await manager.StopAsync(port.Id, null, TestContext.Current.CancellationToken);

        Assert.Equal(PortSessionStopStatus.Failed, stop.Status);
        Assert.NotNull(stop.Error);
        Assert.False(manager.IsRunning(port.Id));

        PortSessionStartResult restart = await manager.StartAsync(port, TestContext.Current.CancellationToken);

        Assert.Equal(PortSessionStartStatus.Started, restart.Status);
        Assert.True(manager.IsRunning(port.Id));
    }

    [Fact]
    public async Task StopAll_AttemptsEverySessionAndReportsAggregateFailures()
    {
        Dictionary<Guid, ScriptedModem> modems = [];
        ModemFactory factory = new(port =>
        {
            ScriptedModem modem = new() { ThrowOnStop = port.Name == "Broken" };
            modems[port.Id] = modem;
            return modem;
        });
        await using PortSessionManager manager = TestSessionManager.Create(factory);

        PortConfig[] ports =
        [
            CreateKissPort("Healthy"),
            CreateKissPort("Broken"),
            CreateKissPort("Also healthy")
        ];

        foreach (PortConfig? port in ports)
        {
            await manager.StartAsync(port, TestContext.Current.CancellationToken);
        }

        IReadOnlyList<PortSessionStopResult> results = await manager.StopAllAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ports.Length, results.Count);
        Assert.Equal(2, results.Count(result => result.Status == PortSessionStopStatus.Stopped));
        PortSessionStopResult failure = Assert.Single(results, result => result.Status == PortSessionStopStatus.Failed);
        Assert.Equal(ports[1].Id, failure.PortId);
        Assert.NotNull(failure.Error);
        Assert.All(modems.Values, modem => Assert.Equal(1, modem.DisposeCount));
        Assert.Empty(manager.RunningPortIds);
    }

    [Fact]
    public async Task RemoveAsync_PrunesPerPortRuntimeState()
    {
        ModemFactory factory = new(_ => new ScriptedModem());
        await using PortSessionManager manager = TestSessionManager.Create(factory);
        PortConfig port = CreateKissPort();
        await manager.StartAsync(port, TestContext.Current.CancellationToken);

        PortSessionStopResult removed = await manager.RemoveAsync(port.Id, TestContext.Current.CancellationToken);

        Assert.Equal(PortSessionStopStatus.Stopped, removed.Status);
        Assert.Equal(0, manager.TrackedPortCount);
        Assert.Equal(
            PortSessionStopStatus.NotRunning,
            (await manager.StopAsync(port.Id, null, TestContext.Current.CancellationToken)).Status);

        // Removing runtime state must not poison a later start of the same port.
        Assert.Equal(
            PortSessionStartStatus.Started,
            (await manager.StartAsync(port, TestContext.Current.CancellationToken)).Status);
        Assert.Equal(1, manager.TrackedPortCount);
    }

    [Fact]
    public async Task StopAsync_ForANonRunningPort_ReportsNotRunning()
    {
        await using PortSessionManager manager = TestSessionManager.Create(new ModemFactory(_ => new ScriptedModem()));
        PortConfig port = CreateKissPort();
        await manager.StartAsync(port, TestContext.Current.CancellationToken);
        await manager.StopAsync(port.Id, null, TestContext.Current.CancellationToken);

        PortSessionStopResult again = await manager.StopAsync(port.Id, null, TestContext.Current.CancellationToken);

        Assert.Equal(PortSessionStopStatus.NotRunning, again.Status);
    }

    [Fact]
    public async Task SendAsync_ForANonRunningPort_ReportsNotRunning()
    {
        await using PortSessionManager manager = TestSessionManager.Create(new ModemFactory(_ => new ScriptedModem()));
        PortConfig port = CreateKissPort();

        PortSessionSendStatus status = await manager.SendAsync(port.Id, CreatePacket(), TestContext.Current.CancellationToken);

        Assert.Equal(PortSessionSendStatus.NotRunning, status);
    }

    [Fact]
    public async Task StartAsync_WhenTheModemFails_AttachesTheOriginalException()
    {
        InvalidOperationException expected = new("transport refused");
        await using PortSessionManager manager = TestSessionManager.Create(
            new ModemFactory(_ => new ScriptedModem { StartException = expected }));
        PortConfig port = CreateKissPort();

        PortSessionStartResult result = await manager.StartAsync(port, TestContext.Current.CancellationToken);

        Assert.Equal(PortSessionStartStatus.Failed, result.Status);
        Assert.Same(expected, result.Error);
        Assert.False(result.IsRunning);
        Assert.False(manager.IsRunning(port.Id));
    }

    [Fact]
    public async Task Subscribers_ReceivePacketsAndFaultsFromTheOwningPort()
    {
        ScriptedModem modem = new();
        await using PortSessionManager manager = TestSessionManager.Create(new ModemFactory(_ => modem));
        PortConfig port = CreateKissPort();
        TaskCompletionSource<PortPacketReceivedEventArgs> packetArgs = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<PortTransportFaultEventArgs> faultArgs = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        manager.PacketReceived += (_, args) => packetArgs.TrySetResult(args);
        manager.TransportFaulted += (_, args) => faultArgs.TrySetResult(args);

        await manager.StartAsync(port, TestContext.Current.CancellationToken);

        PositionPacket packet = CreatePacket();
        modem.RaisePacketReceived(packet);
        InvalidOperationException fault = new("socket lost");
        modem.RaiseReceiveError(fault);

        PortPacketReceivedEventArgs received = await packetArgs.Task.WaitAsync(
            TestTimeouts.TestTimeout,
            TestContext.Current.CancellationToken);
        Assert.Equal(port.Id, received.PortId);
        Assert.Same(packet, received.Packet);

        PortTransportFaultEventArgs reported = await faultArgs.Task.WaitAsync(
            TestTimeouts.TestTimeout,
            TestContext.Current.CancellationToken);
        Assert.Equal(port.Id, reported.PortId);
        Assert.Same(fault, reported.Exception);
    }

    private static PortConfig CreateKissPort(string name = "KISS") => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        IsEnabled = false,
        IsRx = true,
        IsTx = true,
        TypeSettings = new KissSettings
        {
            Transport = new TcpKissTransportSettings { Host = "127.0.0.1", Port = 8001 }
        }
    };

    private static PositionPacket CreatePacket() => new()
    {
        Source = new Callsign("N0CALL", 1),
        Destination = new Callsign("APRS"),
        Location = new Coordinate(0, 0)
    };

    private sealed class ModemFactory(Func<PortConfig, IPortModem> create) : IModemFactory
    {
        private int _createCount;

        public int CreateCount => Volatile.Read(ref _createCount);

        public Task<IPortModem> CreateAsync(PortConfig port, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _createCount);
            return Task.FromResult(create(port));
        }
    }

    private sealed class ScriptedModem : IPortModem
    {
        private readonly TaskCompletionSource _sendEntered =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _sendRelease =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _disposeCount;

        public string TransportName => "Scripted";

        public event EventHandler<AprsPacket>? PacketReceived;

        public event EventHandler<Exception>? ReceiveError;

        public bool BlockSends { get; init; }

        public Exception? StartException { get; init; }

        public bool ThrowOnStop { get; init; }

        public int StopCount { get; private set; }

        public int DisposeCount => Volatile.Read(ref _disposeCount);

        public bool LastSendObservedDisposedModem { get; private set; }

        public Task SendEntered => _sendEntered.Task;

        public void ReleaseSend() => _sendRelease.TrySetResult();

        public void Start()
        {
            if (StartException is not null)
            {
                throw StartException;
            }
        }

        public void RaisePacketReceived(AprsPacket packet) => PacketReceived?.Invoke(this, packet);

        public void RaiseReceiveError(Exception exception) => ReceiveError?.Invoke(this, exception);

        public Task StopAsync()
        {
            StopCount++;

            return ThrowOnStop
                ? Task.FromException(new InvalidOperationException("stop refused"))
                : Task.CompletedTask;
        }

        public async Task SendAsync(AprsPacket packet, CancellationToken cancellationToken = default)
        {
            LastSendObservedDisposedModem = DisposeCount > 0;

            if (!BlockSends)
            {
                return;
            }

            _sendEntered.TrySetResult();
            await _sendRelease.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        public ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref _disposeCount);
            return ValueTask.CompletedTask;
        }
    }
}
