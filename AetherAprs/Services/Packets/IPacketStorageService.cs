// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Models.Aprs;
using AetherAprs.Models.Aprs.Packets;
using AetherAprs.Models.Messaging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace AetherAprs.Services.Packets;

/// <summary>Persists APRS packets and their delivery state.</summary>
public interface IPacketStorageService
{
    /// <summary>Stores a received or transmitted packet.</summary>
    Task StorePacketAsync(AprsPacket packet, Guid? portId, bool isOutbound = false, CancellationToken cancellationToken = default);

    /// <summary>Stores an outbound logical message once in a SQLite write transaction.</summary>
    Task EnsureOutboundMessageAsync(
        MessagePacket packet, Guid? portId, int retryCount = 0,
        DateTimeOffset? nextRetryTime = null, Guid? logicalId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Updates delivery state for an outbound logical message.</summary>
    Task UpdateOutboundMessageStatusAsync(
        AprsAddress addressee, int messageNumber, MessageDeliveryStatus deliveryStatus, int retryCount,
        DateTimeOffset? nextRetryTime, CancellationToken cancellationToken = default);

    /// <summary>Removes packets beyond the configured retention limits.</summary>
    Task CleanupOldPacketsAsync(CancellationToken cancellationToken = default);

    /// <summary>Raised after a newly stored packet is committed.</summary>
    event EventHandler? PacketStored;
}
