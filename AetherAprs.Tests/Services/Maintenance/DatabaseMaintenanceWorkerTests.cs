// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Threading;
using System.Threading.Tasks;

using AetherAprs.Services.Maintenance;
using AetherAprs.Services.Packets;
using AetherAprs.Tests.Helpers;

using Microsoft.Extensions.Logging.Abstractions;

using NSubstitute;

using Xunit;

namespace AetherAprs.Tests.Services.Maintenance;

public sealed class DatabaseMaintenanceWorkerTests
{
    [Fact]
    public async Task StartAsync_RunsTheFirstSweepWhenTheDelayIsZero()
    {
        IPacketStorageService storage = Substitute.For<IPacketStorageService>();
        TaskCompletionSource swept = new(TaskCreationOptions.RunContinuationsAsynchronously);
        storage.CleanupOldPacketsAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                swept.TrySetResult();
                return Task.CompletedTask;
            });

        TestClock clock = new();
        await using DatabaseMaintenanceWorker worker = CreateWorker(
            storage,
            clock,
            interval: TimeSpan.FromHours(1),
            initialDelay: TimeSpan.Zero);

        await worker.StartAsync(TestContext.Current.CancellationToken);
        await swept.Task.WaitAsync(TestTimeouts.TestTimeout, TestContext.Current.CancellationToken);

        Assert.True(worker.IsRunning);
        await storage.Received(1).CleanupOldPacketsAsync(Arg.Any<CancellationToken>());

        // The sweep is done; the only progress left is the interval delay.
        await clock.WaitForTimerAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1, clock.PendingTimerCount);
    }

    [Fact]
    public async Task StartAsync_DoesNotSweepBeforeTheInitialDelay()
    {
        IPacketStorageService storage = Substitute.For<IPacketStorageService>();
        TaskCompletionSource swept = new(TaskCreationOptions.RunContinuationsAsynchronously);
        storage.CleanupOldPacketsAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                swept.TrySetResult();
                return Task.CompletedTask;
            });

        TestClock clock = new();
        await using DatabaseMaintenanceWorker worker = CreateWorker(
            storage,
            clock,
            interval: TimeSpan.FromHours(2),
            initialDelay: TimeSpan.FromHours(1));

        await worker.StartAsync(TestContext.Current.CancellationToken);

        // The loop created its delay timer, so time is the only thing that can
        // make it sweep. No real waiting is involved.
        await clock.WaitForTimerAsync(TestContext.Current.CancellationToken);
        Assert.True(worker.IsRunning);
        await storage.DidNotReceive().CleanupOldPacketsAsync(Arg.Any<CancellationToken>());

        clock.Advance(TimeSpan.FromHours(1) - TimeSpan.FromSeconds(1));
        await storage.DidNotReceive().CleanupOldPacketsAsync(Arg.Any<CancellationToken>());

        clock.Advance(TimeSpan.FromSeconds(1));
        await swept.Task.WaitAsync(TestTimeouts.TestTimeout, TestContext.Current.CancellationToken);
        await storage.Received(1).CleanupOldPacketsAsync(Arg.Any<CancellationToken>());
        Assert.Equal(clock.UtcNow, TestClock.DefaultStart.AddHours(1));
    }

    [Fact]
    public async Task StartAsync_Twice_KeepsOneLoop()
    {
        IPacketStorageService storage = Substitute.For<IPacketStorageService>();
        TestClock clock = new();
        await using DatabaseMaintenanceWorker worker = CreateWorker(
            storage,
            clock,
            interval: TimeSpan.FromHours(1),
            initialDelay: TimeSpan.FromHours(1));

        await worker.StartAsync(TestContext.Current.CancellationToken);
        await worker.StartAsync(TestContext.Current.CancellationToken);

        await clock.WaitForTimerAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1, clock.PendingTimerCount);
        Assert.True(worker.IsRunning);
    }

    [Fact]
    public async Task FailedSweep_DoesNotStopTheWorker()
    {
        IPacketStorageService storage = Substitute.For<IPacketStorageService>();
        int sweeps = 0;
        TaskCompletionSource secondSweep = new(TaskCreationOptions.RunContinuationsAsynchronously);
        storage.CleanupOldPacketsAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                if (Interlocked.Increment(ref sweeps) == 1)
                {
                    return Task.FromException(new InvalidOperationException("database is locked"));
                }

                secondSweep.TrySetResult();
                return Task.CompletedTask;
            });

        TestClock clock = new();
        await using DatabaseMaintenanceWorker worker = CreateWorker(
            storage,
            clock,
            interval: TimeSpan.FromMinutes(5),
            initialDelay: TimeSpan.Zero);

        await worker.StartAsync(TestContext.Current.CancellationToken);

        // The first sweep fails synchronously, then the loop waits one interval.
        await clock.WaitForTimerAsync(TestContext.Current.CancellationToken);
        clock.Advance(TimeSpan.FromMinutes(5));
        await secondSweep.Task.WaitAsync(TestTimeouts.TestTimeout, TestContext.Current.CancellationToken);

        Assert.True(worker.IsRunning);
        Assert.Equal(2, sweeps);
    }

    [Fact]
    public async Task DisposeAsync_StopsTheLoop()
    {
        IPacketStorageService storage = Substitute.For<IPacketStorageService>();
        TestClock clock = new();
        DatabaseMaintenanceWorker worker = CreateWorker(
            storage,
            clock,
            interval: TimeSpan.FromSeconds(1),
            initialDelay: TimeSpan.FromHours(1));

        await worker.StartAsync(TestContext.Current.CancellationToken);
        await clock.WaitForTimerAsync(TestContext.Current.CancellationToken);
        Assert.True(worker.IsRunning);

        await worker.DisposeAsync();

        Assert.False(worker.IsRunning);
        clock.Advance(TimeSpan.FromHours(1));
        await storage.DidNotReceive().CleanupOldPacketsAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartAsync_AfterDispose_Throws()
    {
        DatabaseMaintenanceWorker worker = CreateWorker(
            Substitute.For<IPacketStorageService>(),
            new TestClock(),
            interval: TimeSpan.FromHours(1),
            initialDelay: TimeSpan.FromHours(1));

        await worker.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            worker.StartAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Constructor_RejectsAnInvalidSchedule()
    {
        IPacketStorageService storage = Substitute.For<IPacketStorageService>();
        NullLogger<DatabaseMaintenanceWorker> logger = NullLogger<DatabaseMaintenanceWorker>.Instance;

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new DatabaseMaintenanceWorker(storage, logger, interval: TimeSpan.Zero));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new DatabaseMaintenanceWorker(storage, logger, initialDelay: TimeSpan.FromSeconds(-1)));
    }

    private static DatabaseMaintenanceWorker CreateWorker(
        IPacketStorageService storage,
        TimeProvider timeProvider,
        TimeSpan interval,
        TimeSpan initialDelay) =>
        new(
            storage,
            NullLogger<DatabaseMaintenanceWorker>.Instance,
            timeProvider,
            interval: interval,
            initialDelay: initialDelay);
}
