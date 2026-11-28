// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Threading;
using System.Threading.Tasks;

using AetherAprs.Models.Aprs;
using AetherAprs.Models.Aprs.Packets;
using AetherAprs.Models.Messaging;

namespace AetherAprs.Services.Packets;

/// <summary>
/// Describes a write that failed because the SQLite volume ran out of room or
/// hit an I/O limit.
/// </summary>
/// <param name="operation">The storage operation that failed.</param>
/// <param name="exception">The provider exception that reported the pressure.</param>
public sealed class StoragePressureEventArgs(string operation, Exception exception) : EventArgs
{
    /// <summary>Gets the storage operation that failed.</summary>
    public string Operation { get; } = operation;

    /// <summary>Gets the provider exception that reported the pressure.</summary>
    public Exception Exception { get; } = exception;
}

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

    /// <summary>
    /// Raised when a write fails because the volume is full or read-only, so
    /// callers and diagnostics can report pressure instead of a generic error.
    /// </summary>
    event EventHandler<StoragePressureEventArgs>? StoragePressureDetected;
}
