// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Threading;
using System.Threading.Tasks;

using AetherAprs.Android.Services.Transports;

using Xunit;

namespace AetherAprs.Tests.Android;

/// <summary>
/// Regression tests: a blocking native connect must be bounded
/// by a timeout and must observe cancellation instead of blocking the caller.
/// </summary>
public sealed class BlockingConnectTimeoutTests
{
    [Fact]
    public async Task RunAsync_ConnectBlocksPastTimeout_ThrowsTimeoutException()
    {
        using ManualResetEventSlim release = new();
        try
        {
            Task connect = BlockingConnectTimeout.RunAsync(
                () => release.Wait(TimeSpan.FromSeconds(30)),
                TimeSpan.FromMilliseconds(100),
                TestContext.Current.CancellationToken);

            await Assert.ThrowsAsync<TimeoutException>(() =>
                connect.WaitAsync(TestTimeouts.TestTimeout, TestContext.Current.CancellationToken));
        }
        finally
        {
            release.Set();
        }
    }

    [Fact]
    public async Task RunAsync_CancelledBeforeTimeout_ThrowsOperationCanceledException()
    {
        using ManualResetEventSlim release = new();
        using CancellationTokenSource cts = new();
        try
        {
            Task connect = BlockingConnectTimeout.RunAsync(
                () => release.Wait(TimeSpan.FromSeconds(30)),
                TimeSpan.FromSeconds(30),
                cts.Token);

            cts.CancelAfter(TimeSpan.FromMilliseconds(50));

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                connect.WaitAsync(TestTimeouts.TestTimeout, TestContext.Current.CancellationToken));
        }
        finally
        {
            release.Set();
        }
    }

    [Fact]
    public async Task RunAsync_ConnectCompletes_ReturnsWithoutError()
    {
        bool completed = false;

        await BlockingConnectTimeout.RunAsync(
            () => completed = true,
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);

        Assert.True(completed);
    }
}
