// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Threading;
using System.Threading.Tasks;
using AetherAprs.Services.Platform;
using Xunit;

namespace AetherAprs.Tests.Services.Platform;

public sealed class ProcessLifetimeFlagTests
{
    [Fact]
    public void TrySet_ReportsOnlyTheFirstCaller()
    {
        ProcessLifetimeFlag flag = new();

        Assert.False(flag.IsSet);
        Assert.True(flag.TrySet());
        Assert.True(flag.IsSet);
        Assert.False(flag.TrySet());
    }

    [Fact]
    public void TrySet_IsAtomicUnderConcurrentCallers()
    {
        ProcessLifetimeFlag flag = new();
        int winners = 0;

        Parallel.For(0, 64, _ =>
        {
            if (flag.TrySet())
            {
                Interlocked.Increment(ref winners);
            }
        });

        Assert.Equal(1, Volatile.Read(ref winners));
        Assert.True(flag.IsSet);
    }
}
