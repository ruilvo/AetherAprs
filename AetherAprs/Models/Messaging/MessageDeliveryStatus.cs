// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

namespace AetherAprs.Models.Messaging;

/// <summary>
/// Delivery status for outbound APRS messages.
/// </summary>
public enum MessageDeliveryStatus
{
    /// <summary>
    /// Message is pending delivery/acknowledgment.
    /// </summary>
    Pending,

    /// <summary>
    /// Message was acknowledged by the recipient.
    /// </summary>
    Acknowledged,

    /// <summary>
    /// Message was rejected by the recipient.
    /// </summary>
    Rejected,

    /// <summary>
    /// Message timed out waiting for acknowledgment.
    /// </summary>
    Timeout
}
