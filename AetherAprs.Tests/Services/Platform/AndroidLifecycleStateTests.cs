// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Collections.Concurrent;
using AetherAprs.Services.Platform;
using Xunit;

namespace AetherAprs.Tests.Services.Platform;

public sealed class AndroidLifecycleStateTests
{
    [Fact]
    public void ActivityRecreation_PreservesObservableForegroundAndPermissionOutcomes()
    {
        var lifecycle = new AndroidLifecycleState();
        var states = new ConcurrentQueue<AndroidLifecycleSnapshot>();
        lifecycle.StateChanged += (_, snapshot) => states.Enqueue(snapshot);

        lifecycle.ProcessStarted();
        lifecycle.ActivityCreated();
        lifecycle.ActivityResumed();
        lifecycle.PermissionResult(AndroidPermission.Notifications, true);
        lifecycle.ForegroundStartRequested();
        lifecycle.ForegroundStarted();
        lifecycle.NotificationPosted();
        lifecycle.ActivityPaused();
        lifecycle.ActivityDestroyed();
        lifecycle.ActivityCreated();
        lifecycle.ActivityResumed();

        var snapshot = lifecycle.Snapshot;
        Assert.True(snapshot.ProcessAlive);
        Assert.Equal(AndroidActivityState.Resumed, snapshot.ActivityState);
        Assert.Equal(AndroidForegroundState.Running, snapshot.ForegroundState);
        Assert.True(snapshot.NotificationPosted);
        Assert.True(snapshot.Permissions[AndroidPermission.Notifications]);
        Assert.Contains(states, state => state.ActivityState == AndroidActivityState.Destroyed);
    }

    [Fact]
    public void ProcessDeath_RecordsKilledForegroundAndTransport()
    {
        var lifecycle = new AndroidLifecycleState();
        lifecycle.ProcessStarted();
        lifecycle.ForegroundStarted();
        lifecycle.TransportConnected();
        lifecycle.ProcessStopped();

        var snapshot = lifecycle.Snapshot;
        Assert.False(snapshot.ProcessAlive);
        Assert.Equal(AndroidForegroundState.Killed, snapshot.ForegroundState);
        Assert.Equal(AndroidTransportState.Unknown, snapshot.TransportState);
        Assert.Equal(AndroidActivityState.Destroyed, snapshot.ActivityState);
    }

    [Fact]
    public void ForegroundRejectionAndPermissionRevocationRemainObservable()
    {
        var lifecycle = new AndroidLifecycleState();
        lifecycle.ForegroundStartRequested();
        lifecycle.ForegroundRejected();
        lifecycle.PermissionResult(AndroidPermission.Bluetooth, false);
        lifecycle.TransportReconnectStarted();
        lifecycle.TransportFaulted();

        var snapshot = lifecycle.Snapshot;
        Assert.Equal(AndroidForegroundState.Rejected, snapshot.ForegroundState);
        Assert.False(snapshot.Permissions[AndroidPermission.Bluetooth]);
        Assert.Equal(AndroidTransportState.Degraded, snapshot.TransportState);
    }
}
