// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Models.Aprs;
using AetherAprs.Models.Messaging;
using System;

namespace AetherAprs.Data;

internal static class MessageRecordMapper
{
    public static MessageRecord ToRecord(StoredMessage message)
    {
        return new MessageRecord
        {
            Peer = message.Peer.ToString(),
            Text = message.Text,
            Timestamp = message.Timestamp,
            IsOutbound = message.IsOutbound,
            MessageNumber = message.MessageNumber,
            PortId = message.PortId,
            DeliveryStatus = message.DeliveryStatus.HasValue ? (int)message.DeliveryStatus.Value : null,
            RetryCount = message.RetryCount,
            NextRetryTime = message.NextRetryTime
        };
    }

    public static StoredMessage? ToStoredMessage(MessageRecord record)
    {
        if (!Callsign.TryParse(record.Peer, out var peer))
        {
            return null;
        }

        return new StoredMessage
        {
            Peer = peer,
            Text = record.Text,
            Timestamp = record.Timestamp,
            IsOutbound = record.IsOutbound,
            MessageNumber = record.MessageNumber,
            PortId = record.PortId,
            DeliveryStatus = record.DeliveryStatus.HasValue && Enum.IsDefined(typeof(MessageDeliveryStatus), record.DeliveryStatus.Value)
                ? (MessageDeliveryStatus)record.DeliveryStatus.Value
                : null,
            RetryCount = record.RetryCount,
            NextRetryTime = record.NextRetryTime
        };
    }
}
