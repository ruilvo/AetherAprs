// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Threading.Tasks;

namespace AetherAprs.Services.Platform;

/// <summary>
/// Reasons that can independently require the application to be kept alive
/// (foreground service notification, wake lock, battery-optimization exemption).
/// </summary>
public enum KeepAliveReason
{
    /// <summary>At least one port (RF/APRS-IS) is actively running.</summary>
    Ports,

    /// <summary>Location tracking is enabled.</summary>
    LocationTracking
}

/// <summary>
/// Serialized state machine over the platform <see cref="IForegroundService"/>.
/// Multiple independent sources (ports, location tracking) can hold the app
/// alive; the underlying foreground service runs exactly while at least one
/// reason is held.
/// </summary>
/// <remarks>
/// <para>
/// Acquire and release are applied one at a time, and the platform calls happen
/// inside that order, so a release can never race a start. A release that
/// arrives while the service is starting is applied after the start completes
/// and stops the service; either order converges on the set of reasons that are
/// actually held.
/// </para>
/// <para>
/// A platform refusal never loses a reason: the hold becomes
/// <see cref="KeepAliveState.Degraded"/> and the next acquire retries. Callers
/// therefore treat <see cref="AcquireAsync"/> as idempotent intent rather than a
/// one-shot command.
/// </para>
/// </remarks>
public interface IKeepAliveService
{
    /// <summary>Current hold state; reading it never blocks.</summary>
    KeepAliveStatus Status { get; }

    /// <summary>Raised when <see cref="Status"/> changes.</summary>
    event EventHandler<KeepAliveStatusChangedEventArgs>? StatusChanged;

    /// <summary>
    /// Registers an active keep-alive reason. Starts the foreground service the
    /// first time a reason becomes active, refreshes the declared types when a
    /// further reason arrives, and retries a degraded hold.
    /// </summary>
    Task<KeepAliveStatus> AcquireAsync(KeepAliveReason reason);

    /// <summary>
    /// Clears a keep-alive reason. Stops the foreground service once no reasons
    /// remain. Releasing a reason that is not held is a no-op.
    /// </summary>
    Task<KeepAliveStatus> ReleaseAsync(KeepAliveReason reason);
}
