// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

namespace AetherAprs.Models.Messaging;

/// <summary>
/// Delivery status for outbound APRS messages.
/// </summary>
/// <remarks>
/// The numeric values are persisted in SQLite, so existing members keep their
/// values and new members are appended.
/// </remarks>
public enum MessageDeliveryStatus
{
    /// <summary>
    /// Message is pending delivery/acknowledgment.
    /// </summary>
    Pending = 0,

    /// <summary>
    /// Message was acknowledged by the recipient.
    /// </summary>
    Acknowledged = 1,

    /// <summary>
    /// Message was rejected by the recipient.
    /// </summary>
    Rejected = 2,

    /// <summary>
    /// Message timed out waiting for acknowledgment.
    /// </summary>
    Timeout = 3,

    /// <summary>
    /// Message could not be sent (no TX port or the initial send failed entirely).
    /// </summary>
    Failed = 4,

    /// <summary>
    /// Message is waiting for a free wire number; it has not been sent.
    /// </summary>
    Queued = 5,

    /// <summary>
    /// Message expired before it was delivered.
    /// </summary>
    Expired = 6,

    /// <summary>
    /// A transmission attempt is in flight. Transient: it is never persisted and
    /// the durable record stays <see cref="Pending"/> while the wire is used.
    /// </summary>
    Sending = 7,
}
