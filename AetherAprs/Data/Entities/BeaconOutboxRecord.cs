// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;

using AetherAprs.Services.Transmission;

namespace AetherAprs.Data.Entities;

/// <summary>
/// Durable work item for one beacon waiting to reach a modem.
/// </summary>
/// <remarks>
/// The row keeps the position that was captured when the beacon was created, so a
/// queued beacon is never sent with a newer fix than the operator saw. A null
/// <see cref="PortId"/> means the beacon is waiting for any usable transmit route.
/// </remarks>
public sealed class BeaconOutboxRecord
{
    /// <summary>Logical identity of the beacon.</summary>
    public Guid Id { get; set; }

    /// <summary>Target port, or null when any usable transmit route will do.</summary>
    public Guid? PortId { get; set; }

    public double Latitude { get; set; }

    public double Longitude { get; set; }

    /// <summary>Altitude in meters above sea level, when the fix carried one.</summary>
    public double? Altitude { get; set; }

    /// <summary>UTC instant the beacon was queued.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>UTC instant after which the beacon is no longer worth sending.</summary>
    public DateTime ExpiresAt { get; set; }

    public int AttemptCount { get; set; }

    public BeaconQueueStatus Status { get; set; }

    /// <summary>
    /// Last failure reason, or the reason a beacon was dropped or expired. Never
    /// contains packet payloads or positions.
    /// </summary>
    public string? Error { get; set; }

    /// <summary>
    /// UTC instant of the last state change, used to order reconciliation and to
    /// prune terminal rows.
    /// </summary>
    public DateTime UpdatedAt { get; set; }
}
