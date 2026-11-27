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
        PortConfig source = CreatePort("RF");
        PortConfig target = CreatePort("Target");
        TaskCompletionSource sendEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource releaseSend = new(TaskCreationOptions.RunContinuationsAsynchronously);

        IPortSessionManager sessionManager = CreateSessionManager(source.Id, target.Id);
        sessionManager
            .SendAsync(target.Id, Arg.Any<AprsPacket>(), Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                sendEntered.TrySetResult();
                await releaseSend.Task.ConfigureAwait(false);
                return PortSessionSendStatus.Sent;
            });

        IDigipeaterService digipeater = CreateDigipeater(target.Id);
        await using DigipeatRouter router = CreateRouter(
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
        PortConfig source = CreatePort("RF");
        PortConfig target = CreatePort("Target");
        TestClock time = new(DateTimeOffset.UnixEpoch);
        TaskCompletionSource firstDecisionEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource releaseFirstDecision = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int decisionCount = 0;

        IPortSessionManager sessionManager = CreateSessionManager(source.Id, target.Id);
        sessionManager
            .SendAsync(Arg.Any<Guid>(), Arg.Any<AprsPacket>(), Arg.Any<CancellationToken>())
            .Returns(PortSessionSendStatus.Sent);

        IDigipeaterService digipeater = Substitute.For<IDigipeaterService>();
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

        await using DigipeatRouter router = CreateRouter(
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
        PortConfig source = CreatePort("RF");
        PortConfig target = CreatePort("Target");

        IPortSessionManager sessionManager = Substitute.For<IPortSessionManager>();
        sessionManager.IsRunning(source.Id).Returns(true);
        sessionManager.IsRunning(target.Id).Returns(false);

        await using DigipeatRouter router = CreateRouter(
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
        PortConfig source = CreatePort("RF");
        PortConfig target = CreatePort("Target");
        TaskCompletionSource<PortPacketTransmittedEventArgs> forwarded = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        IPortSessionManager sessionManager = CreateSessionManager(source.Id, target.Id);
        sessionManager
            .SendAsync(Arg.Any<Guid>(), Arg.Any<AprsPacket>(), Arg.Any<CancellationToken>())
            .Returns(PortSessionSendStatus.Sent);

        await using DigipeatRouter router = CreateRouter(
            source,
            target,
            sessionManager,
            CreateDigipeater(target.Id));

        router.PacketForwarded += (_, args) => forwarded.TrySetResult(args);
        router.Enqueue(source.Id, CreatePacket());

        PortPacketTransmittedEventArgs args = await forwarded.Task.WaitAsync(
            TestTimeouts.TestTimeout,
            TestContext.Current.CancellationToken);

        Assert.Equal(target.Id, args.PortId);
        Assert.Equal(1, router.GetMetrics().Forwarded);
        Assert.Equal(0, router.GetMetrics().Discarded);
    }

    [Fact]
    public async Task TransmitFailure_IsCountedAsFailedAndNotForwarded()
    {
        PortConfig source = CreatePort("RF");
        PortConfig target = CreatePort("Target");

        IPortSessionManager sessionManager = CreateSessionManager(source.Id, target.Id);
        sessionManager
            .SendAsync(Arg.Any<Guid>(), Arg.Any<AprsPacket>(), Arg.Any<CancellationToken>())
            .Returns<Task<PortSessionSendStatus>>(_ =>
                Task.FromException<PortSessionSendStatus>(new InvalidOperationException("socket lost")));

        await using DigipeatRouter router = CreateRouter(
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
        PortConfig source = CreatePort("RF");
        PortConfig target = CreatePort("Target");

        await using DigipeatRouter router = CreateRouter(
            source,
            target,
            CreateSessionManager(source.Id, target.Id),
            CreateDigipeater(target.Id));

        router.Enqueue(Guid.NewGuid(), CreatePacket());

        await WaitForAsync(
            () => router.TotalDiscardedCandidates == 1,
            "A candidate from an unknown source must be discarded.");
    }

    [Fact]
    public async Task DiscardedCandidate_IsRecordedInTheSharedDecisionHistory()
    {
        PortConfig source = CreatePort("RF");
        PortConfig target = CreatePort("Target");
        DigipeatDecisionLog decisionLog = new();

        await using DigipeatRouter router = CreateRouter(
            source,
            target,
            CreateSessionManager(source.Id, target.Id),
            CreateDigipeater(target.Id),
            decisionLog: decisionLog);

        Guid unknownSource = Guid.NewGuid();
        router.Enqueue(unknownSource, CreatePacket());

        await WaitForAsync(
            () => router.GetDiscardedCandidateCount(unknownSource) == 1,
            "A candidate from an unknown source must be discarded.");

        DigipeatDecisionRecord record = Assert.Single(router.GetRecentDecisions());
        Assert.Equal(unknownSource, record.SourcePortId);
        Assert.Equal(DigipeatReason.SourcePortUnknown, record.Reason);
        Assert.Equal(1, router.GetDecisionCount(DigipeatReason.SourcePortUnknown));
    }

    [Fact]
    public async Task Enqueue_BeforeStart_IsForwardedOnceTheRouterStarts()
    {
        PortConfig source = CreatePort("Source");
        PortConfig target = CreatePort("Target");
        IPortSessionManager sessionManager = CreateSessionManager(source.Id, target.Id);
        IDigipeaterService digipeater = CreateDigipeater(target.Id);

        await using DigipeatRouter router = new(
            new FakeConfigurationSource(source, target),
            sessionManager,
            digipeater,
            NullLogger<DigipeatRouter>.Instance);

        router.Enqueue(source.Id, CreatePacket());

        // Construction only wires the queue; the startup phase starts the drain, so
        // nothing can be forwarded until StartAsync runs.
        Assert.Equal(0, router.GetMetrics().Forwarded);
        await sessionManager.DidNotReceive().SendAsync(
            Arg.Any<Guid>(),
            Arg.Any<AprsPacket>(),
            Arg.Any<CancellationToken>());

        await router.StartAsync(TestContext.Current.CancellationToken);
        await WaitForAsync(
            () => router.GetMetrics().Forwarded == 1,
            "The queued candidate should be forwarded once the router starts.");
    }

    private static DigipeatRouter CreateRouter(
        PortConfig source,
        PortConfig target,
        IPortSessionManager sessionManager,
        IDigipeaterService digipeater,
        DigipeatRouterOptions? options = null,
        TimeProvider? timeProvider = null,
        DigipeatDecisionLog? decisionLog = null)
    {
        DigipeatRouter router = new(
            new FakeConfigurationSource(source, target),
            sessionManager,
            digipeater,
            NullLogger<DigipeatRouter>.Instance,
            options: options,
            timeProvider: timeProvider,
            decisionLog: decisionLog);

        // The consumer is a startup phase, so the helper starts it explicitly.
        // StartAsync completes synchronously.
        router.StartAsync().GetAwaiter().GetResult();
        return router;
    }

    private static IPortSessionManager CreateSessionManager(Guid sourceId, Guid targetId)
    {
        IPortSessionManager sessionManager = Substitute.For<IPortSessionManager>();
        sessionManager.IsRunning(sourceId).Returns(true);
        sessionManager.IsRunning(targetId).Returns(true);
        return sessionManager;
    }

    private static IDigipeaterService CreateDigipeater(Guid targetId)
    {
        IDigipeaterService digipeater = Substitute.For<IDigipeaterService>();
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
        DateTime deadline = DateTime.UtcNow + TestTimeouts.TestTimeout;
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
}
