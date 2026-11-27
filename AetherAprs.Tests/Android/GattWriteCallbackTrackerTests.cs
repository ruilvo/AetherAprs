// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using AetherAprs.Android.Services.Transports;
using Xunit;

namespace AetherAprs.Tests.Android;

/// <summary>
/// Regression tests: Android delivers the GATT write acknowledgement with no
/// correlation identifier, so a late callback for an abandoned write must never be
/// mistaken for the acknowledgement of the next write.
/// </summary>
public sealed class GattWriteCallbackTrackerTests
{
    [Fact]
    public void Register_ThenComplete_MatchesTheSameWrite()
    {
        GattWriteCallbackTracker tracker = new();

        object token = tracker.Register();
        GattWriteCallback callback = tracker.Complete();

        Assert.Same(token, callback.Token);
        Assert.False(callback.IsStale);
        Assert.False(callback.IsAbandoned);
        Assert.False(callback.ReleasesGate);
        Assert.False(tracker.HasPendingWrite);
    }

    [Fact]
    public void Complete_WithNothingPending_IsStaleAndReleasesNothing()
    {
        GattWriteCallbackTracker tracker = new();

        GattWriteCallback callback = tracker.Complete();

        Assert.True(callback.IsStale);
        Assert.Null(callback.Token);
        Assert.False(callback.ReleasesGate);
    }

    [Fact]
    public void Register_WhileAWriteIsPending_Throws()
    {
        GattWriteCallbackTracker tracker = new();
        tracker.Register();

        // Two ATT writes in flight at once would make every acknowledgement ambiguous,
        // so the second registration is a programming error rather than a retry.
        Assert.Throws<InvalidOperationException>(() => tracker.Register());
    }

    [Fact]
    public void Abandon_KeepsTheGateHeldUntilTheLateCallbackArrives()
    {
        GattWriteCallbackTracker tracker = new();
        object token = tracker.Register();

        Assert.True(tracker.Abandon(token));

        // Still pending: the abandoned write's callback has not arrived, so the gate
        // must stay held and no new write may start.
        Assert.True(tracker.HasPendingWrite);

        GattWriteCallback callback = tracker.Complete();

        Assert.Same(token, callback.Token);
        Assert.True(callback.IsAbandoned);
        Assert.True(callback.ReleasesGate);
        Assert.False(tracker.HasPendingWrite);
    }

    [Fact]
    public void Abandon_WithAStaleToken_DoesNotAffectThePendingWrite()
    {
        GattWriteCallbackTracker tracker = new();
        object abandoned = tracker.Register();
        tracker.TryCancel(abandoned);
        object current = tracker.Register();

        Assert.False(tracker.Abandon(abandoned));

        GattWriteCallback callback = tracker.Complete();

        Assert.Same(current, callback.Token);
        Assert.False(callback.IsAbandoned);
        Assert.False(callback.ReleasesGate);
    }

    [Fact]
    public void TryCancel_DropsARegistrationThatNeverReachedTheStack()
    {
        GattWriteCallbackTracker tracker = new();
        object token = tracker.Register();

        Assert.True(tracker.TryCancel(token));
        Assert.False(tracker.HasPendingWrite);

        // No callback will arrive for a write the stack never accepted, so the next
        // write may register immediately.
        object next = tracker.Register();
        Assert.Same(next, tracker.Complete().Token);
    }

    [Fact]
    public void CallbackArrivingAfterACancel_IsStale()
    {
        GattWriteCallbackTracker tracker = new();
        object token = tracker.Register();
        tracker.TryCancel(token);

        // A cancelled registration means the stack never accepted the write, so any
        // callback at this point belongs to nothing the stream is waiting for.
        GattWriteCallback callback = tracker.Complete();

        Assert.True(callback.IsStale);
        Assert.False(callback.ReleasesGate);
    }

    [Fact]
    public void AbandonThenClear_ReportsTheHeldGate()
    {
        GattWriteCallbackTracker tracker = new();
        object token = tracker.Register();
        tracker.Abandon(token);

        Assert.True(tracker.Clear());
        Assert.False(tracker.HasPendingWrite);
        Assert.False(tracker.Clear());
    }

    [Fact]
    public void Tokens_AreUniquePerRegistration()
    {
        GattWriteCallbackTracker tracker = new();

        object first = tracker.Register();
        tracker.Complete();
        object second = tracker.Register();

        Assert.NotSame(first, second);
        Assert.NotEqual(first, second);
    }
}
