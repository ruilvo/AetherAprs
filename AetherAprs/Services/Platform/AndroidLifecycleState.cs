// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Threading;

namespace AetherAprs.Services.Platform;

public enum AndroidActivityState
{
    Unknown,
    Created,
    Resumed,
    Paused,
    Destroyed
}

public enum AndroidForegroundState
{
    Stopped,
    Starting,
    Running,
    Rejected,
    Killed
}

public enum AndroidTransportState
{
    Unknown,
    Connected,
    Degraded,
    Reconnecting
}

public enum AndroidPermission
{
    Notifications,
    Bluetooth,
    Location,
    BackgroundLocation,
    Audio
}

public sealed record AndroidLifecycleSnapshot(
    bool ProcessAlive,
    AndroidActivityState ActivityState,
    AndroidForegroundState ForegroundState,
    AndroidTransportState TransportState,
    bool NotificationPosted,
    IReadOnlyDictionary<AndroidPermission, bool> Permissions);

/// <summary>
/// Records Android lifecycle outcomes that are otherwise difficult for the
/// shared UI and diagnostics layer to observe.
/// </summary>
public sealed class AndroidLifecycleState
{
    private readonly Lock _gate = new();
    private readonly Dictionary<AndroidPermission, bool> _permissions = [];
    private AndroidLifecycleSnapshot _snapshot = new(
        false,
        AndroidActivityState.Unknown,
        AndroidForegroundState.Stopped,
        AndroidTransportState.Unknown,
        false,
        new Dictionary<AndroidPermission, bool>());

    public AndroidLifecycleSnapshot Snapshot
    {
        get
        {
            lock (_gate)
            {
                return CopySnapshot(_snapshot);
            }
        }
    }

    public event EventHandler<AndroidLifecycleSnapshot>? StateChanged;

    public void ProcessStarted() =>
        Update(snapshot => snapshot.ProcessAlive
            ? snapshot
            : snapshot with
            {
                ProcessAlive = true,
                ForegroundState = AndroidForegroundState.Stopped,
                TransportState = AndroidTransportState.Unknown,
                NotificationPosted = false
            });

    public void ActivityCreated() =>
        Update(snapshot => snapshot with { ActivityState = AndroidActivityState.Created });

    public void ActivityResumed() =>
        Update(snapshot => snapshot with { ActivityState = AndroidActivityState.Resumed });

    public void ActivityPaused() =>
        Update(snapshot => snapshot with { ActivityState = AndroidActivityState.Paused });

    public void ActivityDestroyed() =>
        Update(snapshot => snapshot with { ActivityState = AndroidActivityState.Destroyed });

    public void ProcessStopped() =>
        Update(snapshot => snapshot with
        {
            ProcessAlive = false,
            ActivityState = AndroidActivityState.Destroyed,
            ForegroundState = AndroidForegroundState.Killed,
            TransportState = AndroidTransportState.Unknown
        });

    public void ForegroundStartRequested() =>
        Update(snapshot => snapshot with { ForegroundState = AndroidForegroundState.Starting });

    public void ForegroundStarted() =>
        Update(snapshot => snapshot with { ForegroundState = AndroidForegroundState.Running });

    public void ForegroundRejected() =>
        Update(snapshot => snapshot with { ForegroundState = AndroidForegroundState.Rejected });

    public void ForegroundStopped() =>
        Update(snapshot => snapshot with { ForegroundState = AndroidForegroundState.Stopped });

    public void PermissionResult(AndroidPermission permission, bool granted)
    {
        lock (_gate)
        {
            _permissions[permission] = granted;
        }

        Publish(snapshot => snapshot with
        {
            Permissions = new Dictionary<AndroidPermission, bool>(_permissions)
        });
    }

    public void NotificationPosted() =>
        Update(snapshot => snapshot with { NotificationPosted = true });

    public void TransportConnected() =>
        Update(snapshot => snapshot with { TransportState = AndroidTransportState.Connected });

    public void TransportFaulted() =>
        Update(snapshot => snapshot with { TransportState = AndroidTransportState.Degraded });

    public void TransportReconnectStarted() =>
        Update(snapshot => snapshot with { TransportState = AndroidTransportState.Reconnecting });

    private void Update(Func<AndroidLifecycleSnapshot, AndroidLifecycleSnapshot> update) => Publish(update);

    private void Publish(Func<AndroidLifecycleSnapshot, AndroidLifecycleSnapshot> update)
    {
        AndroidLifecycleSnapshot snapshot;
        lock (_gate)
        {
            _snapshot = update(_snapshot) with
            {
                Permissions = new Dictionary<AndroidPermission, bool>(_permissions)
            };
            snapshot = CopySnapshot(_snapshot);
        }

        StateChanged?.Invoke(this, snapshot);
    }

    private static AndroidLifecycleSnapshot CopySnapshot(AndroidLifecycleSnapshot snapshot) =>
        snapshot with
        {
            Permissions = new Dictionary<AndroidPermission, bool>(snapshot.Permissions)
        };
}
