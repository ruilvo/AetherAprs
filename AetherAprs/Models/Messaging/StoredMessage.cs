// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Models.Aprs;
using System;

namespace AetherAprs.Models.Messaging;

/// <summary>
/// A stored APRS message belonging to a conversation.
/// </summary>
public sealed class StoredMessage
{
    public required Callsign Peer { get; init; }

    public required string Text { get; init; }

    public required DateTimeOffset Timestamp { get; init; }

    public required bool IsOutbound { get; init; }

    public int? MessageNumber { get; init; }

    public Guid? PortId { get; init; }

    /// <summary>
    /// Delivery status for outbound messages. Null for inbound messages.
    /// </summary>
    public MessageDeliveryStatus? DeliveryStatus { get; set; }

    /// <summary>
    /// Number of retry attempts for outbound messages.
    /// </summary>
    public int RetryCount { get; set; }

    /// <summary>
    /// Next retry time for outbound messages.
    /// </summary>
    public DateTimeOffset? NextRetryTime { get; set; }

    /// <summary>
    /// Gets the delivery status text for display. Empty for inbound messages or when no status is available.
    /// </summary>
    public string DeliveryStatusText
    {
        get
        {
            if (!IsOutbound || !DeliveryStatus.HasValue)
            {
                return string.Empty;
            }

            return DeliveryStatus.Value switch
            {
                MessageDeliveryStatus.Pending => $"⏱ {Localization.Strings.Get("Pending")}",
                MessageDeliveryStatus.Acknowledged => $"✓ {Localization.Strings.Get("Acknowledged")}",
                MessageDeliveryStatus.Rejected => $"✗ {Localization.Strings.Get("Rejected")}",
                MessageDeliveryStatus.Timeout => $"⌛ {Localization.Strings.Get("Timeout")}",
                _ => string.Empty
            };
        }
    }
}
