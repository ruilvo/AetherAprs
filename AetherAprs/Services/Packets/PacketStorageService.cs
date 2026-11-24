// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Data;
using AetherAprs.Data.Entities;
using AetherAprs.Data.Mappers;
using AetherAprs.Models.Aprs.Packets;
using AetherAprs.Models.Aprs;
using AetherAprs.Models.Messaging;
using AetherAprs.Services.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AetherAprs.Services.Packets;

/// <summary>
/// Service for persisting and managing APRS packets in the database.
/// </summary>
public interface IPacketStorageService
{
    /// <summary>
    /// Stores an APRS packet in the database.
    /// </summary>
    /// <param name="packet">The packet to store.</param>
    /// <param name="portId">The port that received or sent the packet.</param>
    /// <param name="isOutbound">True if this is a sent packet, false if received.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task StorePacketAsync(AprsPacket packet, Guid? portId, bool isOutbound = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores an outbound logical message once. Concurrent calls for the same
    /// message identity are serialized by a SQLite write transaction.
    /// </summary>
    Task EnsureOutboundMessageAsync(
        MessagePacket packet, Guid? portId, int retryCount = 0,
        DateTimeOffset? nextRetryTime = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates delivery state for an outbound logical message.
    /// </summary>
    Task UpdateOutboundMessageStatusAsync(
        AprsAddress addressee,
        int messageNumber,
        MessageDeliveryStatus deliveryStatus,
        int retryCount,
        DateTimeOffset? nextRetryTime,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes packets older than the configured retention period.
    /// </summary>
    Task CleanupOldPacketsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Raised when a packet is successfully stored in the database.
    /// </summary>
    event EventHandler? PacketStored;

}

public sealed class StoragePressureEventArgs(string operation, Exception exception) : EventArgs
{
    public string Operation { get; } = operation;

    public Exception Exception { get; } = exception;
}

public sealed class PacketStorageService(
    IDbContextFactory<AppDbContext> dbContextFactory,
    IConfigurationService configurationService,
    ILogger<PacketStorageService> logger) : IPacketStorageService
{
    private const int CleanupBatchSize = 250;

    public event EventHandler? PacketStored;

    public event EventHandler<StoragePressureEventArgs>? StoragePressureDetected;

    public async Task StorePacketAsync(AprsPacket packet, Guid? portId, bool isOutbound = false, CancellationToken cancellationToken = default)
    {
        if (isOutbound && packet is MessagePacket { MessageNumber: not null } message)
        {
            await EnsureOutboundMessageAsync(message, portId, cancellationToken: cancellationToken).ConfigureAwait(false);
            return;
        }

        try
        {
            logger.LogDebug("Storing {Direction} packet from {Source}, Type: {Type}",
                isOutbound ? "outbound" : "inbound", packet.Source, packet.GetType().Name);

            await using var context = await dbContextFactory.CreateDbContextAsync(cancellationToken);

            var record = PacketRecordMapper.ToRecord(packet, portId, DateTimeOffset.UtcNow, isOutbound);
            context.Packets.Add(record);

            await context.SaveChangesAsync(cancellationToken);

            logger.LogDebug("Stored {Direction} {PacketType} packet from {Source}",
                isOutbound ? "outbound" : "inbound", record.PacketType, record.SourceBase);
        }
        catch (Exception ex)
        {
            if (ex is Microsoft.Data.Sqlite.SqliteException { SqliteErrorCode: 5 or 13 or 14 })
            {
                StoragePressureDetected?.Invoke(
                    this,
                    new StoragePressureEventArgs("store", ex));
                await CleanupOldPacketsAsync(cancellationToken).ConfigureAwait(false);
            }

            logger.LogError(ex, "Failed to store packet from {Source}", packet.Source);
            throw; // Re-throw to make errors more visible
        }

        NotifyPacketStored();
    }

    public async Task EnsureOutboundMessageAsync(
        MessagePacket packet,
        Guid? portId,
        int retryCount = 0,
        DateTimeOffset? nextRetryTime = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(packet);
        if (packet.MessageNumber is not { } number)
        {
            throw new ArgumentException("A logical outbound message must have a message number.", nameof(packet));
        }

        await using (var context = await dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
        {
            // Microsoft.Data.Sqlite starts a non-deferred write transaction here.
            // Reserve the writer before the lookup, including across store instances.
            await using var transaction = await context.Database
                .BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken).ConfigureAwait(false);
            var records = await FindOutboundMessage(context, packet.Addressee, number)
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            if (records.Count == 0)
            {
                var record = PacketRecordMapper.ToRecord(packet, portId, DateTimeOffset.UtcNow, isOutbound: true);
                record.Message!.RetryCount = retryCount;
                record.Message.NextRetryTime = nextRetryTime?.UtcDateTime;
                context.Packets.Add(record);
            }
            else
            {
                foreach (var record in records)
                {
                    var message = record.Message!;
                    if (message.DeliveryStatus == MessageDeliveryStatus.Pending && retryCount > message.RetryCount)
                    {
                        message.RetryCount = retryCount;
                        message.NextRetryTime = nextRetryTime?.UtcDateTime;
                    }
                }
            }

            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

            if (records.Count > 0)
            {
                return;
            }
        }

        NotifyPacketStored();
    }

    private void NotifyPacketStored()
    {
        if (PacketStored is not { } handlers)
        {
            return;
        }

        foreach (EventHandler handler in handlers.GetInvocationList())
        {
            try
            {
                handler(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "PacketStored subscriber failed after commit.");
            }
        }
    }

    public async Task UpdateOutboundMessageStatusAsync(
        AprsAddress addressee,
        int messageNumber,
        MessageDeliveryStatus deliveryStatus,
        int retryCount,
        DateTimeOffset? nextRetryTime,
        CancellationToken cancellationToken = default)
    {
        await using var context = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database
            .BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken).ConfigureAwait(false);
        var records = await FindOutboundMessage(context, addressee, messageNumber)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var record in records)
        {
            var message = record.Message!;
            if (message.DeliveryStatus != MessageDeliveryStatus.Pending)
            {
                continue;
            }

            message.DeliveryStatus = deliveryStatus;
            if (retryCount >= message.RetryCount)
            {
                message.RetryCount = retryCount;
                message.NextRetryTime = nextRetryTime?.UtcDateTime;
            }
        }

        if (records.Count > 0)
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task CleanupOldPacketsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var retentionDays = configurationService.Settings.Aprs.PacketRetentionDays;
            var cutoffDate = DateTime.UtcNow.AddDays(-retentionDays);

            await using var context = await dbContextFactory.CreateDbContextAsync(cancellationToken);

            var removedByAge = await DeleteEligibleBatchesAsync(
                context,
                packet => packet.Timestamp < cutoffDate,
                cancellationToken).ConfigureAwait(false);

            var maximumRows = Math.Max(1, configurationService.Settings.Aprs.PacketRetentionMaxRows);
            var removedByBound = 0;
            var currentRows = await context.Packets.CountAsync(cancellationToken).ConfigureAwait(false);
            while (currentRows > maximumRows)
            {
                var removed = await DeleteOldestEligibleBatchAsync(
                    context,
                    Math.Min(CleanupBatchSize, currentRows - maximumRows),
                    cancellationToken).ConfigureAwait(false);
                if (removed == 0)
                {
                    logger.LogWarning(
                        "Packet storage remains above {MaximumRows} rows because pending outbound messages are protected.",
                        maximumRows);
                    break;
                }

                removedByBound += removed;
                currentRows -= removed;
            }

            if (removedByAge + removedByBound > 0)
            {
                logger.LogInformation(
                    "Cleaned up {Count} packet records ({AgeCount} expired, {BoundCount} over storage bound).",
                    removedByAge + removedByBound,
                    removedByAge,
                    removedByBound);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to cleanup old packets");
        }
    }

    private static async Task<int> DeleteEligibleBatchesAsync(
        AppDbContext context,
        System.Linq.Expressions.Expression<Func<Data.Entities.PacketRecord, bool>> predicate,
        CancellationToken cancellationToken)
    {
        var total = 0;
        while (true)
        {
            var ids = await context.Packets
                .Where(predicate)
                .Where(IsSafeToDelete)
                .OrderBy(packet => packet.Timestamp)
                .ThenBy(packet => packet.Id)
                .Select(packet => packet.Id)
                .Take(CleanupBatchSize)
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            if (ids.Count == 0)
            {
                return total;
            }

            total += await context.Packets
                .Where(packet => ids.Contains(packet.Id))
                .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task<int> DeleteOldestEligibleBatchAsync(
        AppDbContext context,
        int batchSize,
        CancellationToken cancellationToken)
    {
        var ids = await context.Packets
            .Where(IsSafeToDelete)
            .OrderBy(packet => packet.Timestamp)
            .ThenBy(packet => packet.Id)
            .Select(packet => packet.Id)
            .Take(batchSize)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return ids.Count == 0
            ? 0
            : await context.Packets
                .Where(packet => ids.Contains(packet.Id))
                .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
    }

    private static readonly System.Linq.Expressions.Expression<Func<Data.Entities.PacketRecord, bool>> IsSafeToDelete =
        packet => !packet.IsOutbound ||
            packet.Message == null ||
            packet.Message.DeliveryStatus != MessageDeliveryStatus.Pending;

    private static IQueryable<PacketRecord> FindOutboundMessage(
        AppDbContext context, AprsAddress addressee, int messageNumber)
    {
        var ssid = (AprsSsid)addressee.Ssid.GetValueOrDefault();
        return context.Packets.Where(packet =>
            packet.IsOutbound && packet.PacketType == PacketTypes.Message &&
            packet.Message != null && packet.Message.Number == messageNumber &&
            packet.Message.AddresseeBase == addressee.Base && packet.Message.AddresseeSsid == ssid);
    }
}
