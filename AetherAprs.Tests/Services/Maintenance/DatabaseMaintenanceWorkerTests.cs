// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AetherAprs.Services.Maintenance;
using AetherAprs.Services.Packets;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace AetherAprs.Tests.Services.Maintenance;

public sealed class DatabaseMaintenanceWorkerTests
{
    [Fact]
    public async Task StartAsync_RunsTheFirstSweepWhenTheDelayIsZero()
    {
        var storage = Substitute.For<IPacketStorageService>();
        var swept = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        storage.CleanupOldPacketsAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                swept.TrySetResult();
                return Task.CompletedTask;
            });

        await using var worker = CreateWorker(
            storage,
            interval: TimeSpan.FromHours(1),
            initialDelay: TimeSpan.Zero);

        await worker.StartAsync(TestContext.Current.CancellationToken);
        await swept.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.True(worker.IsRunning);
        await storage.Received(1).CleanupOldPacketsAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartAsync_DoesNotSweepBeforeTheInitialDelay()
    {
        var storage = Substitute.For<IPacketStorageService>();

        await using var worker = CreateWorker(
            storage,
            interval: TimeSpan.FromHours(1),
            initialDelay: TimeSpan.FromHours(1));

        await worker.StartAsync(TestContext.Current.CancellationToken);
        await Task.Delay(100, TestContext.Current.CancellationToken);

        Assert.True(worker.IsRunning);
        await storage.DidNotReceive().CleanupOldPacketsAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartAsync_Twice_KeepsOneLoop()
    {
        var storage = Substitute.For<IPacketStorageService>();

        await using var worker = CreateWorker(
            storage,
            interval: TimeSpan.FromHours(1),
            initialDelay: TimeSpan.Zero);

        await worker.StartAsync(TestContext.Current.CancellationToken);
        await worker.StartAsync(TestContext.Current.CancellationToken);
        await EventuallyAsync(() => storage.ReceivedCalls().Any());
        await Task.Delay(50, TestContext.Current.CancellationToken);

        await storage.Received(1).CleanupOldPacketsAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FailedSweep_DoesNotStopTheWorker()
    {
        var storage = Substitute.For<IPacketStorageService>();
        var sweeps = 0;
        var secondSweep = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
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

        await using var worker = CreateWorker(
            storage,
            interval: TimeSpan.FromMilliseconds(25),
            initialDelay: TimeSpan.Zero);

        await worker.StartAsync(TestContext.Current.CancellationToken);
        await secondSweep.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.True(worker.IsRunning);
    }

    [Fact]
    public async Task DisposeAsync_StopsTheLoop()
    {
        var storage = Substitute.For<IPacketStorageService>();
        var worker = CreateWorker(
            storage,
            interval: TimeSpan.FromSeconds(1),
            initialDelay: TimeSpan.FromHours(1));

        await worker.StartAsync(TestContext.Current.CancellationToken);
        Assert.True(worker.IsRunning);

        await worker.DisposeAsync();

        Assert.False(worker.IsRunning);
    }

    [Fact]
    public async Task StartAsync_AfterDispose_Throws()
    {
        var worker = CreateWorker(
            Substitute.For<IPacketStorageService>(),
            interval: TimeSpan.FromHours(1),
            initialDelay: TimeSpan.FromHours(1));

        await worker.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            worker.StartAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Constructor_RejectsAnInvalidSchedule()
    {
        var storage = Substitute.For<IPacketStorageService>();
        var logger = NullLogger<DatabaseMaintenanceWorker>.Instance;

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new DatabaseMaintenanceWorker(storage, logger, interval: TimeSpan.Zero));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new DatabaseMaintenanceWorker(storage, logger, initialDelay: TimeSpan.FromSeconds(-1)));
    }

    private static DatabaseMaintenanceWorker CreateWorker(
        IPacketStorageService storage,
        TimeSpan interval,
        TimeSpan initialDelay) =>
        new(storage, NullLogger<DatabaseMaintenanceWorker>.Instance, interval: interval, initialDelay: initialDelay);

    private static async Task EventuallyAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("The condition was not met before the deadline.");
            }

            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }
}
