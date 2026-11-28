// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;

using AetherAprs.Models.Aprs;

namespace AetherAprs.Models.Messaging;

/// <summary>
/// A stored APRS message belonging to a conversation.
/// </summary>
public sealed class StoredMessage
{
    public required AprsAddress Peer { get; init; }

    public required string Text { get; init; }

    public required DateTimeOffset Timestamp { get; init; }

    public required bool IsOutbound { get; init; }

    public int? MessageNumber { get; set; }

    /// <summary>
    /// Logical message identity shared with the durable outbox record.
    /// </summary>
    public Guid? LogicalId { get; init; }

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
    /// Retries allowed after the initial attempt, captured when the message is
    /// created so the detail text stays stable if settings change later.
    /// </summary>
    public int MaxRetries { get; set; }

    /// <summary>
    /// True while the message has no enabled TX route; it stays pending instead
    /// of being marked failed or losing its place.
    /// </summary>
    public bool NoRoute { get; set; }

    /// <summary>
    /// Name of the port that last carried the message, when known.
    /// </summary>
    public string? RouteName { get; set; }

    /// <summary>
    /// Ports that carried the same inbound message. A duplicate heard on another
    /// port is folded into the existing message instead of being dropped, so the
    /// route count survives deduplication.
    /// </summary>
    public List<Guid> ReceivedPortIds { get; } = [];

    public int ReceivedPortCount => ReceivedPortIds.Count;

    /// <summary>
    /// Route and attempt detail for outbound messages: how many attempts were
    /// made, and which port carried them. Empty for inbound messages.
    /// </summary>
    public string DeliveryDetailText
    {
        get
        {
            if (!IsOutbound)
            {
                return ReceivedPortCount > 1
                    ? Localization.Strings.Format("MessageDetailHeardOn", ReceivedPortCount)
                    : string.Empty;
            }

            if (!DeliveryStatus.HasValue)
            {
                return string.Empty;
            }

            List<string> parts = [];
            switch (DeliveryStatus.Value)
            {
                case MessageDeliveryStatus.Queued:
                    parts.Add(Localization.Strings.Get("MessageDetailQueued"));
                    break;
                case MessageDeliveryStatus.Sending:
                case MessageDeliveryStatus.Pending:
                    parts.Add(Localization.Strings.Format(
                        "MessageDetailAttempts",
                        RetryCount + 1,
                        MaxRetries + 1));
                    break;
                default:
                    break;
            }

            if (!string.IsNullOrEmpty(RouteName))
            {
                parts.Add(Localization.Strings.Format("MessageDetailRoute", RouteName));
            }

            if (NoRoute)
            {
                parts.Add(Localization.Strings.Get("MessageDetailNoRoute"));
            }

            return string.Join(" · ", parts);
        }
    }

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
                MessageDeliveryStatus.Queued => $"⏱ {Localization.Strings.Get("Queued")}",
                MessageDeliveryStatus.Sending => $"↑ {Localization.Strings.Get("Sending")}",
                MessageDeliveryStatus.Pending => $"⏱ {Localization.Strings.Get("Pending")}",
                MessageDeliveryStatus.Acknowledged => $"✓ {Localization.Strings.Get("Acknowledged")}",
                MessageDeliveryStatus.Rejected => $"✗ {Localization.Strings.Get("Rejected")}",
                MessageDeliveryStatus.Timeout => $"⌛ {Localization.Strings.Get("Timeout")}",
                MessageDeliveryStatus.Expired => $"⌛ {Localization.Strings.Get("Expired")}",
                MessageDeliveryStatus.Failed => $"✗ {Localization.Strings.Get("Failed")}",
                _ => string.Empty
            };
        }
    }
}
