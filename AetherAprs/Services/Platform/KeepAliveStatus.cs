// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Linq;

namespace AetherAprs.Services.Platform;

/// <summary>
/// Reported state of the platform keep-alive hold.
/// </summary>
public enum KeepAliveState
{
    /// <summary>No reason is held and the foreground service is not running.</summary>
    Stopped,

    /// <summary>The foreground service is running for every held reason.</summary>
    Active,

    /// <summary>
    /// A reason is held but the platform did not honour it, or the platform is
    /// still running after every reason was released. The application keeps
    /// working; the hold simply cannot be guaranteed.
    /// </summary>
    Degraded,

    /// <summary>The last reason was released and the foreground service is stopping.</summary>
    Stopping
}

/// <summary>
/// Snapshot of the keep-alive hold: which reasons are held, which foreground
/// service types they require, and whether the platform honoured the request.
/// </summary>
public sealed class KeepAliveStatus
{
    private KeepAliveStatus(
        KeepAliveState state,
        IReadOnlyList<KeepAliveReason> reasons,
        ForegroundServiceType requiredTypes,
        string? degradedReason)
    {
        State = state;
        Reasons = reasons;
        RequiredTypes = requiredTypes;
        DegradedReason = degradedReason;
    }

    public KeepAliveState State { get; }

    /// <summary>Reasons currently held, ordered so snapshots are comparable.</summary>
    public IReadOnlyList<KeepAliveReason> Reasons { get; }

    /// <summary>Foreground service types the held reasons require.</summary>
    public ForegroundServiceType RequiredTypes { get; }

    /// <summary>
    /// Why the hold is degraded, or <see langword="null"/> when it is not. The
    /// text is diagnostic only and never contains user data.
    /// </summary>
    public string? DegradedReason { get; }

    public static KeepAliveStatus Stopped(ForegroundServiceType requiredTypes) =>
        new(KeepAliveState.Stopped, [], requiredTypes, null);

    public static KeepAliveStatus Active(
        IEnumerable<KeepAliveReason> reasons,
        ForegroundServiceType requiredTypes) =>
        new(KeepAliveState.Active, Snapshot(reasons), requiredTypes, null);

    public static KeepAliveStatus Degraded(
        IEnumerable<KeepAliveReason> reasons,
        ForegroundServiceType requiredTypes,
        string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        return new(KeepAliveState.Degraded, Snapshot(reasons), requiredTypes, reason);
    }

    public static KeepAliveStatus Stopping(
        IEnumerable<KeepAliveReason> reasons,
        ForegroundServiceType requiredTypes) =>
        new(KeepAliveState.Stopping, Snapshot(reasons), requiredTypes, null);

    /// <summary>
    /// Whether two snapshots report the same hold. Sequences are compared by
    /// value so a repeated transition with unchanged reasons is not published.
    /// </summary>
    public bool DescribesSameHold(KeepAliveStatus other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return State == other.State
            && RequiredTypes == other.RequiredTypes
            && string.Equals(DegradedReason, other.DegradedReason, StringComparison.Ordinal)
            && Reasons.SequenceEqual(other.Reasons);
    }

    private static KeepAliveReason[] Snapshot(IEnumerable<KeepAliveReason> reasons)
    {
        ArgumentNullException.ThrowIfNull(reasons);
        return [.. reasons.Distinct().OrderBy(reason => reason)];
    }
}

/// <summary>Raised when the keep-alive hold changes state.</summary>
/// <remarks>
/// Handlers run on the thread that performed the transition, inside the
/// transition gate. They must not block or call back into
/// <see cref="IKeepAliveService"/> synchronously; queue the work instead.
/// </remarks>
public sealed class KeepAliveStatusChangedEventArgs : EventArgs
{
    public required KeepAliveStatus Status { get; init; }
}
