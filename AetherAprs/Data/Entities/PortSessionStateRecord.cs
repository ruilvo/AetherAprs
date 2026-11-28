// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;

using AetherAprs.Services.Ports;

namespace AetherAprs.Data.Entities;

/// <summary>
/// Persisted runtime status of one configured port. The row is diagnostic state
/// only: a restarted process reconnects on explicit operator action, never
/// because a row said the port was connected.
/// </summary>
public sealed class PortSessionStateRecord
{
    public Guid PortId { get; set; }

    public PortSessionState State { get; set; }

    /// <summary>UTC instant of the last received packet, if any.</summary>
    public DateTime? LastReceiveUtc { get; set; }

    /// <summary>UTC instant of the last transmission, if any.</summary>
    public DateTime? LastTransmitUtc { get; set; }

    public int RetryCount { get; set; }

    /// <summary>UTC instant of the next scheduled reconnect attempt.</summary>
    public DateTime? NextRetryUtc { get; set; }

    public string? Error { get; set; }
}
