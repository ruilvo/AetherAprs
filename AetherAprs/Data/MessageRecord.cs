// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;

namespace AetherAprs.Data;

/// <summary>
/// Persisted APRS message belonging to a conversation.
/// </summary>
public sealed class MessageRecord
{
    public long Id { get; set; }

    public string Peer { get; set; } = string.Empty;

    public string Text { get; set; } = string.Empty;

    public DateTimeOffset Timestamp { get; set; }

    public bool IsOutbound { get; set; }

    public int? MessageNumber { get; set; }

    public Guid? PortId { get; set; }

    public int? DeliveryStatus { get; set; }

    public int RetryCount { get; set; }

    public DateTimeOffset? NextRetryTime { get; set; }
}
