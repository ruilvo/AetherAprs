// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Data.Entities;
using AetherAprs.Data.Mappers;
using AetherAprs.Models.Aprs.Packets;
using AetherAprs.Models.Aprs;
using AetherAprs.Models.Messaging;
using AetherAprs.Services.Configuration;
using AetherAprs.Services.Packets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace AetherAprs.Data.Packets;

internal sealed class PacketStorageService(
    SqliteRuntimeWriter writer,
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
            logger.LogDebug("Storing {Direction} packet of type {Type}",
                isOutbound ? "outbound" : "inbound", packet.GetType().Name);

            PacketRecord record = PacketRecordMapper.ToRecord(packet, portId, DateTimeOffset.UtcNow, isOutbound);
            await writer.EnqueueAsync("packet.insert", async (context, token) =>
            {
                context.Packets.Add(record);
                return await context.SaveChangesAsync(token).ConfigureAwait(false);
            }, cancellationToken).ConfigureAwait(false);

            logger.LogDebug("Stored {Direction} {PacketType} packet",
                isOutbound ? "outbound" : "inbound", record.PacketType);
        }
        catch (Exception ex)
        {
            if (ex is Microsoft.Data.Sqlite.SqliteException { SqliteErrorCode: 5 or 13 or 14 })
            {
                NotifyStoragePressure(ex);
                await CleanupOldPacketsAsync(cancellationToken).ConfigureAwait(false);
            }

            logger.LogError(ex, "Failed to store {PacketType} packet", packet.GetType().Name);
            throw; // Re-throw to make errors more visible
        }

        NotifyPacketStored();
    }

    public async Task EnsureOutboundMessageAsync(
        MessagePacket packet,
        Guid? portId,
        int retryCount = 0,
        DateTimeOffset? nextRetryTime = null,
        Guid? logicalId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(packet);
        if (packet.MessageNumber is not { } number)
        {
            throw new ArgumentException("A logical outbound message must have a message number.", nameof(packet));
        }

        PacketRecord newRecord = PacketRecordMapper.ToRecord(
            packet, portId, DateTimeOffset.UtcNow, isOutbound: true, logicalId: logicalId);
        AprsAddress addressee = packet.Addressee;
        bool inserted = await writer.EnqueueAsync("packet.ensure-outbound-message", async (context, token) =>
        {
            List<PacketRecord> records = await (logicalId.HasValue
                    ? FindOutboundMessageByLogicalId(context, logicalId.Value)
                    : FindOutboundMessage(context, addressee, number))
                .ToListAsync(token).ConfigureAwait(false);
            if (records.Count == 0)
            {
                newRecord.Message!.RetryCount = retryCount;
                newRecord.Message.NextRetryTime = nextRetryTime?.UtcDateTime;
                context.Packets.Add(newRecord);
            }
            else
            {
                foreach (PacketRecord? record in records)
                {
                    MessageDataRecord message = record.Message!;
                    if (message.DeliveryStatus == MessageDeliveryStatus.Pending && retryCount > message.RetryCount)
                    {
                        message.RetryCount = retryCount;
                        message.NextRetryTime = nextRetryTime?.UtcDateTime;
                    }
                }
            }

            await context.SaveChangesAsync(token).ConfigureAwait(false);
            return records.Count == 0;
        }, cancellationToken).ConfigureAwait(false);

        if (inserted)
        {
            NotifyPacketStored();
        }
    }

    private void NotifyPacketStored()
    {
        if (PacketStored is not { } handlers)
        {
            return;
        }

        foreach (EventHandler handler in handlers.GetInvocationList().Cast<EventHandler>())
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

    private void NotifyStoragePressure(Exception exception)
    {
        if (StoragePressureDetected is not { } handlers)
        {
            return;
        }

        foreach (EventHandler<StoragePressureEventArgs> handler in handlers.GetInvocationList().Cast<EventHandler<StoragePressureEventArgs>>())
        {
            try
            {
                handler(this, new StoragePressureEventArgs("store", exception));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Storage pressure subscriber failed.");
            }
        }
    }

    public Task UpdateOutboundMessageStatusAsync(
        AprsAddress addressee,
        int messageNumber,
        MessageDeliveryStatus deliveryStatus,
        int retryCount,
        DateTimeOffset? nextRetryTime,
        CancellationToken cancellationToken = default)
        => writer.EnqueueAsync("packet.update-outbound-message", async (context, token) =>
    {
        List<PacketRecord> records = await FindOutboundMessage(context, addressee, messageNumber)
            .ToListAsync(token)
            .ConfigureAwait(false);

        foreach (PacketRecord? record in records)
        {
            MessageDataRecord message = record.Message!;
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

        return records.Count > 0
            ? await context.SaveChangesAsync(token).ConfigureAwait(false)
            : 0;
    }, cancellationToken);

    public async Task CleanupOldPacketsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            int retentionDays = configurationService.Current.Aprs.PacketRetentionDays;
            DateTime cutoffDate = DateTime.UtcNow.AddDays(-retentionDays);

            int maximumRows = Math.Max(1, configurationService.Current.Aprs.PacketRetentionMaxRows);
            int removedByAge = 0;
            while (true)
            {
                int removed = await writer.EnqueueAsync("packet.cleanup-age", (context, token) =>
                    DeleteOldestEligibleBatchAsync(context, context.Packets.Where(packet => packet.Timestamp < cutoffDate),
                        CleanupBatchSize, token), cancellationToken).ConfigureAwait(false);
                removedByAge += removed;
                if (removed < CleanupBatchSize)
                {
                    break;
                }
            }

            int removedByBound = 0;
            while (true)
            {
                (int excess, int removed) = await writer.EnqueueAsync("packet.cleanup-bound", async (context, token) =>
                {
                    int excess = await context.Packets.CountAsync(token).ConfigureAwait(false) - maximumRows;
                    int deleted = excess <= 0 ? 0 : await DeleteOldestEligibleBatchAsync(context, context.Packets,
                        Math.Min(CleanupBatchSize, excess), token).ConfigureAwait(false);
                    return (excess, deleted);
                }, cancellationToken).ConfigureAwait(false);
                if (excess <= 0)
                {
                    break;
                }

                if (removed == 0)
                {
                    logger.LogWarning(
                        "Packet storage remains above {MaximumRows} rows because pending outbound messages are protected.",
                        maximumRows);
                    break;
                }

                removedByBound += removed;
                if (removed == excess)
                {
                    break;
                }
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

    private static async Task<int> DeleteOldestEligibleBatchAsync(
        AppDbContext context,
        IQueryable<PacketRecord> candidates,
        int batchSize,
        CancellationToken cancellationToken)
    {
        List<long> ids = await candidates
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
        AprsSsid ssid = (AprsSsid)addressee.Ssid.GetValueOrDefault();
        return context.Packets.Where(packet =>
            packet.IsOutbound && packet.PacketType == PacketTypes.Message &&
            packet.Message != null && packet.Message.Number == messageNumber &&
            packet.Message.AddresseeBase == addressee.Base && packet.Message.AddresseeSsid == ssid);
    }

    private static IQueryable<PacketRecord> FindOutboundMessageByLogicalId(
        AppDbContext context, Guid logicalId)
        => context.Packets.Where(packet =>
            packet.IsOutbound && packet.PacketType == PacketTypes.Message &&
            packet.Message != null && packet.Message.LogicalId == logicalId);
}
