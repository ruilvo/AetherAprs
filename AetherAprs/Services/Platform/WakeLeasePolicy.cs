// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;

namespace AetherAprs.Services.Platform;

/// <summary>
/// What the foreground service should do with its partial wake lock right now.
/// </summary>
public enum WakeLeaseDecision
{
    /// <summary>The lease already matches the session; nothing to do.</summary>
    None,

    /// <summary>The session has work and no lease is held.</summary>
    Arm,

    /// <summary>The lease is held but the session has nothing to keep awake for.</summary>
    Release
}

/// <summary>
/// Bounds the partial wake lock the Android foreground service holds.
/// </summary>
/// <remarks>
/// A foreground service already keeps the process alive, so the wake lock only buys
/// CPU time while the session is bringing transports up or handling a start request.
/// Acquiring it without a timeout would let a wedged service pin the CPU until the
/// battery dies, which is the pattern the platform warns about. The lease is
/// therefore finite and is re-armed by later start commands instead of being
/// extended on a timer: a service that stops receiving work lets the lease lapse and
/// the platform can then suspend the CPU. Callbacks from the network, Bluetooth and
/// location wake it again, and a foreground service does not need the lock for that.
/// </remarks>
public static class WakeLeasePolicy
{
    /// <summary>
    /// Lease requested from the platform, in milliseconds. Ten minutes covers
    /// transport bring-up and the first exchanges on every port; a longer hold
    /// would be indistinguishable from an unbounded lock to the battery.
    /// </summary>
    public const int LeaseMilliseconds = 10 * 60 * 1000;

    /// <summary>
    /// Decides whether to arm, release or leave the wake lock alone.
    /// </summary>
    /// <param name="isSessionActive">
    /// Whether the session still has work the service is keeping alive.
    /// </param>
    /// <param name="isLeaseHeld">
    /// Whether the platform currently reports the wake lock as held.
    /// </param>
    public static WakeLeaseDecision Decide(bool isSessionActive, bool isLeaseHeld) =>
        (isSessionActive, isLeaseHeld) switch
        {
            (true, false) => WakeLeaseDecision.Arm,
            (false, true) => WakeLeaseDecision.Release,
            _ => WakeLeaseDecision.None
        };

    /// <summary>
    /// Gets the lease as a <see cref="TimeSpan"/> for callers that report or
    /// document the bound.
    /// </summary>
    public static TimeSpan Lease => TimeSpan.FromMilliseconds(LeaseMilliseconds);
}
