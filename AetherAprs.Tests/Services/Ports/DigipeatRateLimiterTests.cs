// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using AetherAprs.Services.Ports;
using AetherAprs.Tests.Helpers;
using Xunit;

namespace AetherAprs.Tests.Services.Ports;

public sealed class DigipeatRateLimiterTests
{
    [Fact]
    public void TryConsume_WithinTheLimit_AllowsEveryForward()
    {
        DigipeatRateLimiter limiter = new(new DigipeatRateLimitOptions
        {
            MaxForwardsPerWindow = 2,
            Window = TimeSpan.FromMinutes(1)
        });
        TestClock clock = new();
        Guid portId = Guid.NewGuid();

        Assert.True(limiter.TryConsume(portId, clock.UtcNow));
        Assert.True(limiter.TryConsume(portId, clock.UtcNow));
        Assert.False(limiter.TryConsume(portId, clock.UtcNow));
    }

    [Fact]
    public void TryConsume_AfterTheWindowElapsed_StartsAFreshWindow()
    {
        DigipeatRateLimiter limiter = new(new DigipeatRateLimitOptions
        {
            MaxForwardsPerWindow = 1,
            Window = TimeSpan.FromMinutes(1)
        });
        TestClock clock = new();
        Guid portId = Guid.NewGuid();

        Assert.True(limiter.TryConsume(portId, clock.UtcNow));
        Assert.False(limiter.TryConsume(portId, clock.UtcNow));

        clock.Advance(TimeSpan.FromMinutes(1));

        Assert.True(limiter.TryConsume(portId, clock.UtcNow));
    }

    [Fact]
    public void TryConsume_OtherPorts_HaveIndependentWindows()
    {
        DigipeatRateLimiter limiter = new(new DigipeatRateLimitOptions
        {
            MaxForwardsPerWindow = 1,
            Window = TimeSpan.FromMinutes(1)
        });
        TestClock clock = new();
        Guid first = Guid.NewGuid();
        Guid second = Guid.NewGuid();

        Assert.True(limiter.TryConsume(first, clock.UtcNow));
        Assert.False(limiter.TryConsume(first, clock.UtcNow));
        Assert.True(limiter.TryConsume(second, clock.UtcNow));
    }

    [Fact]
    public void TryConsume_MorePortsThanTheBound_EvictsTheOldestWindow()
    {
        const int TrackedPorts = 64;

        DigipeatRateLimiter limiter = new(new DigipeatRateLimitOptions
        {
            MaxForwardsPerWindow = 1,
            Window = TimeSpan.FromHours(1)
        });
        TestClock clock = new();

        List<Guid> ports = [];
        for (int index = 0; index < TrackedPorts; index++)
        {
            Guid portId = Guid.NewGuid();
            ports.Add(portId);
            Assert.True(limiter.TryConsume(portId, clock.UtcNow));
            clock.Advance(TimeSpan.FromSeconds(1));
        }

        // The oldest window is still full, so a repeat from it is rejected.
        Assert.False(limiter.TryConsume(ports[0], clock.UtcNow));
        Assert.False(limiter.TryConsume(ports[1], clock.UtcNow));

        // Adding one more tracked port evicts the oldest window instead of growing.
        Guid overflowPort = Guid.NewGuid();
        Assert.True(limiter.TryConsume(overflowPort, clock.UtcNow));

        // The evicted port starts over while the newest window stays limited.
        Assert.True(limiter.TryConsume(ports[0], clock.UtcNow));
        Assert.False(limiter.TryConsume(ports[TrackedPorts - 1], clock.UtcNow));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_NonPositiveLimit_IsRejected(int maxForwards)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DigipeatRateLimiter(new DigipeatRateLimitOptions
        {
            MaxForwardsPerWindow = maxForwards
        }));
    }

    [Fact]
    public void Constructor_NonPositiveWindow_IsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DigipeatRateLimiter(new DigipeatRateLimitOptions
        {
            Window = TimeSpan.Zero
        }));
    }
}
