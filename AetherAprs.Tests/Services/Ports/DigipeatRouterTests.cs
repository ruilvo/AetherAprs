// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Configuration;
using AetherAprs.Models.Aprs;
using AetherAprs.Models.Aprs.Packets;
using AetherAprs.Services.Ports;
using AetherAprs.Tests.Helpers;
using Geo;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace AetherAprs.Tests.Services.Ports;

public sealed class DigipeatRouterTests
{
    [Fact]
    public async Task QueueFull_DiscardsIncomingCandidateInsteadOfReplacingAnother()
    {
        var source = CreatePort("RF");
        var target = CreatePort("Target");
        var sendEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSend = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var sessionManager = CreateSessionManager(source.Id, target.Id);
        sessionManager
            .SendAsync(target.Id, Arg.Any<AprsPacket>(), Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                sendEntered.TrySetResult();
                await releaseSend.Task.ConfigureAwait(false);
                return PortSessionSendStatus.Sent;
            });

        var digipeater = CreateDigipeater(target.Id);
        await using var router = CreateRouter(
            source,
            target,
            sessionManager,
            digipeater,
            new DigipeatRouterOptions { QueueCapacity = 1 });

        router.Enqueue(source.Id, CreatePacket());

        // The consumer is inside the transmit, so its queue slot is free again.
        await sendEntered.Task.WaitAsync(TestTimeouts.TestTimeout, TestContext.Current.CancellationToken);

        router.Enqueue(source.Id, CreatePacket());
        router.Enqueue(source.Id, CreatePacket());

        Assert.Equal(1, router.GetDiscardedCandidateCount(source.Id));
        Assert.Equal(1, router.TotalDiscardedCandidates);
        Assert.Equal(1, router.GetMetrics().Discarded);

        releaseSend.TrySetResult();
        await WaitForAsync(
            () => router.GetMetrics().Forwarded == 2,
            "Both queued candidates must still be forwarded.");
        Assert.Equal(2, router.GetMetrics().Enqueued);
    }

    [Fact]
    public async Task StaleCandidate_IsDiscardedInsteadOfBeingReplayed()
    {
        var source = CreatePort("RF");
        var target = CreatePort("Target");
        var time = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
        var firstDecisionEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstDecision = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var decisionCount = 0;

        var sessionManager = CreateSessionManager(source.Id, target.Id);
        sessionManager
            .SendAsync(Arg.Any<Guid>(), Arg.Any<AprsPacket>(), Arg.Any<CancellationToken>())
            .Returns(PortSessionSendStatus.Sent);

        var digipeater = Substitute.For<IDigipeaterService>();
        digipeater
            .GetDigipeatTargets(
                Arg.Any<AprsPacket>(),
                Arg.Any<Guid>(),
                Arg.Any<bool>(),
                Arg.Any<IReadOnlyList<PortInfo>>())
            .Returns(callInfo =>
            {
                if (Interlocked.Increment(ref decisionCount) == 1)
                {
                    firstDecisionEntered.TrySetResult();

                    // Hold the consumer on the first candidate so the second waits.
                    releaseFirstDecision.Task.Wait(TestTimeouts.TestTimeout);
                }

                return [new DigipeatTarget(target.Id, callInfo.Arg<AprsPacket>())];
            });

        await using var router = CreateRouter(
            source,
            target,
            sessionManager,
            digipeater,
            new DigipeatRouterOptions { MaxCandidateAge = TimeSpan.FromSeconds(5) },
            time);

        router.Enqueue(source.Id, CreatePacket());
        await firstDecisionEntered.Task.WaitAsync(TestTimeouts.TestTimeout, TestContext.Current.CancellationToken);

        // The second candidate waits behind the blocked route while time passes.
        router.Enqueue(source.Id, CreatePacket());
        time.Advance(TimeSpan.FromMinutes(1));
        releaseFirstDecision.TrySetResult();

        await WaitForAsync(
            () => router.GetDiscardedCandidateCount(source.Id) == 1,
            "An aged candidate must be discarded.");
        Assert.Equal(1, router.GetMetrics().Forwarded);
        Assert.Equal(1, router.GetMetrics().Discarded);
    }

    [Fact]
    public async Task TargetNotRunning_DiscardsTheForwardAndNeverStartsThePort()
    {
        var source = CreatePort("RF");
        var target = CreatePort("Target");

        var sessionManager = Substitute.For<IPortSessionManager>();
        sessionManager.IsRunning(source.Id).Returns(true);
        sessionManager.IsRunning(target.Id).Returns(false);

        await using var router = CreateRouter(
            source,
            target,
            sessionManager,
            CreateDigipeater(target.Id));

        router.Enqueue(source.Id, CreatePacket());

        await WaitForAsync(
            () => router.GetDiscardedCandidateCount(source.Id) == 1,
            "A forward to a port that is not running must be discarded.");
        Assert.Equal(0, router.GetMetrics().Forwarded);
        await sessionManager
            .DidNotReceive()
            .SendAsync(Arg.Any<Guid>(), Arg.Any<AprsPacket>(), Arg.Any<CancellationToken>());
        await sessionManager
            .DidNotReceive()
            .StartAsync(Arg.Any<PortConfig>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunningTarget_ReceivesTheForwardAndRaisesPacketForwarded()
    {
        var source = CreatePort("RF");
        var target = CreatePort("Target");
        var forwarded = new TaskCompletionSource<PortPacketTransmittedEventArgs>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var sessionManager = CreateSessionManager(source.Id, target.Id);
        sessionManager
            .SendAsync(Arg.Any<Guid>(), Arg.Any<AprsPacket>(), Arg.Any<CancellationToken>())
            .Returns(PortSessionSendStatus.Sent);

        await using var router = CreateRouter(
            source,
            target,
            sessionManager,
            CreateDigipeater(target.Id));

        router.PacketForwarded += (_, args) => forwarded.TrySetResult(args);
        router.Enqueue(source.Id, CreatePacket());

        var args = await forwarded.Task.WaitAsync(
            TestTimeouts.TestTimeout,
            TestContext.Current.CancellationToken);

        Assert.Equal(target.Id, args.PortId);
        Assert.Equal(1, router.GetMetrics().Forwarded);
        Assert.Equal(0, router.GetMetrics().Discarded);
    }

    [Fact]
    public async Task TransmitFailure_IsCountedAsFailedAndNotForwarded()
    {
        var source = CreatePort("RF");
        var target = CreatePort("Target");

        var sessionManager = CreateSessionManager(source.Id, target.Id);
        sessionManager
            .SendAsync(Arg.Any<Guid>(), Arg.Any<AprsPacket>(), Arg.Any<CancellationToken>())
            .Returns<Task<PortSessionSendStatus>>(_ =>
                Task.FromException<PortSessionSendStatus>(new InvalidOperationException("socket lost")));

        await using var router = CreateRouter(
            source,
            target,
            sessionManager,
            CreateDigipeater(target.Id));

        router.Enqueue(source.Id, CreatePacket());

        await WaitForAsync(
            () => router.GetMetrics().Failed == 1,
            "A failed transmit must be counted.");
        Assert.Equal(0, router.GetMetrics().Forwarded);
    }

    [Fact]
    public async Task SourcePortNoLongerConfigured_DiscardsTheCandidate()
    {
        var source = CreatePort("RF");
        var target = CreatePort("Target");

        await using var router = CreateRouter(
            source,
            target,
            CreateSessionManager(source.Id, target.Id),
            CreateDigipeater(target.Id));

        router.Enqueue(Guid.NewGuid(), CreatePacket());

        await WaitForAsync(
            () => router.TotalDiscardedCandidates == 1,
            "A candidate from an unknown source must be discarded.");
    }

    private static DigipeatRouter CreateRouter(
        PortConfig source,
        PortConfig target,
        IPortSessionManager sessionManager,
        IDigipeaterService digipeater,
        DigipeatRouterOptions? options = null,
        TimeProvider? timeProvider = null) =>
        new(
            new FakeConfigurationSource(source, target),
            sessionManager,
            digipeater,
            NullLogger<DigipeatRouter>.Instance,
            options: options,
            timeProvider: timeProvider);

    private static IPortSessionManager CreateSessionManager(Guid sourceId, Guid targetId)
    {
        var sessionManager = Substitute.For<IPortSessionManager>();
        sessionManager.IsRunning(sourceId).Returns(true);
        sessionManager.IsRunning(targetId).Returns(true);
        return sessionManager;
    }

    private static IDigipeaterService CreateDigipeater(Guid targetId)
    {
        var digipeater = Substitute.For<IDigipeaterService>();
        digipeater
            .GetDigipeatTargets(
                Arg.Any<AprsPacket>(),
                Arg.Any<Guid>(),
                Arg.Any<bool>(),
                Arg.Any<IReadOnlyList<PortInfo>>())
            .Returns(callInfo => [new DigipeatTarget(targetId, callInfo.Arg<AprsPacket>())]);
        return digipeater;
    }

    private static async Task WaitForAsync(Func<bool> condition, string because)
    {
        var deadline = DateTime.UtcNow + TestTimeouts.TestTimeout;
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
            {
                Assert.Fail(because);
            }

            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }

    private static PortConfig CreatePort(string name) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        IsRx = true,
        IsTx = true,
        AllowDigipeat = true,
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

    private sealed class FakeConfigurationSource(params PortConfig[] ports) : IPortConfigurationSource
    {
        public IReadOnlyList<PortConfig> GetPorts() => [.. ports];

        public PortConfig? Find(Guid id) => ports.FirstOrDefault(port => port.Id == id);
    }

    private sealed class ManualTimeProvider(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan delta) => _now += delta;
    }
}
