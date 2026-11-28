// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Android.Services.Platform;

using Xunit;

namespace AetherAprs.Tests.Android;

/// <summary>
/// Regression tests: re-entering compass setup must not register the sensor
/// listeners twice, and stop/dispose must be safe to call repeatedly.
/// </summary>
public sealed class CompassSubscriptionGuardTests
{
    [Fact]
    public void TryStart_RepeatedCalls_OnlyFirstRegisters()
    {
        CompassSubscriptionGuard guard = new();

        Assert.True(guard.TryStart());
        Assert.False(guard.TryStart());
        Assert.True(guard.IsSubscribed);
    }

    [Fact]
    public void TryStop_AfterStart_OnlyFirstUnregisters()
    {
        CompassSubscriptionGuard guard = new();
        guard.TryStart();

        Assert.True(guard.TryStop());
        Assert.False(guard.TryStop());
        Assert.False(guard.IsSubscribed);
    }

    [Fact]
    public void TryStart_AfterStop_RegistersAgain()
    {
        CompassSubscriptionGuard guard = new();
        guard.TryStart();
        guard.TryStop();

        Assert.True(guard.TryStart());
        Assert.True(guard.IsSubscribed);
    }

    [Fact]
    public void TryStop_WithoutStart_IsSafe()
    {
        CompassSubscriptionGuard guard = new();

        Assert.False(guard.TryStop());
        Assert.False(guard.IsSubscribed);
    }

    [Fact]
    public void DisposeOnce_WhileSubscribed_RequestsOneUnregister()
    {
        CompassSubscriptionGuard guard = new();
        guard.TryStart();

        Assert.True(guard.DisposeOnce());
        Assert.False(guard.DisposeOnce());
        Assert.True(guard.IsDisposed);
        Assert.False(guard.IsSubscribed);
    }

    [Fact]
    public void DisposeOnce_WithoutSubscribing_RequestsNoUnregister()
    {
        CompassSubscriptionGuard guard = new();

        Assert.False(guard.DisposeOnce());
    }

    [Fact]
    public void TryStart_AfterDispose_IsRefused()
    {
        CompassSubscriptionGuard guard = new();
        guard.DisposeOnce();

        Assert.False(guard.TryStart());
        Assert.False(guard.IsSubscribed);
    }

    [Fact]
    public void TryStop_AfterDispose_IsSafe()
    {
        CompassSubscriptionGuard guard = new();
        guard.TryStart();
        guard.DisposeOnce();

        Assert.False(guard.TryStop());
    }
}
