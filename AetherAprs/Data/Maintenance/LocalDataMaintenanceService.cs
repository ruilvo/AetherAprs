// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Data.Entities;
using AetherAprs.Data.Mappers;
using AetherAprs.Models.Messaging;
using AetherAprs.Services.Diagnostics;
using AetherAprs.Services.Maintenance;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

namespace AetherAprs.Data.Maintenance;

/// <summary>
/// Clears one category of local data at a time through the serialized runtime
/// writer.
/// </summary>
/// <remarks>
/// Every category is defined by the rows only it owns, so clearing one never
/// touches another. Packet and outbox deletes run in bounded batches: the clear
/// may remove thousands of rows, but no single statement holds the database long
/// enough to block packet ingestion or a UI read.
/// </remarks>
internal sealed class LocalDataMaintenanceService(
    SqliteRuntimeWriter writer,
    IDiagnosticEventSink diagnostics,
    ILogger<LocalDataMaintenanceService> logger) : ILocalDataMaintenanceService
{
    /// <summary>Most rows removed per statement, so a clear stays responsive.</summary>
    internal const int ClearBatchSize = 500;

    /// <summary>Outbox rows that still represent durable, unsent work.</summary>
    private static readonly Expression<Func<OutboxMessageRecord, bool>> IsOpenOutboxWork =
        message => message.Status == MessageDeliveryStatus.Pending
            || message.Status == MessageDeliveryStatus.Queued;

    public Task<LocalDataClearResult> ClearPacketHistoryAsync(CancellationToken cancellationToken = default)
        => ClearAsync(
            LocalDataCategory.PacketHistory,
            "maintenance.clear-packet-history",
            (context, token) => DeletePacketBatchesAsync(
                context,
                context.Packets.Where(packet => packet.PacketType != PacketTypes.Message
                    && packet.PacketType != PacketTypes.MessageAck
                    && packet.PacketType != PacketTypes.MessageRej),
                token),
            cancellationToken);

    public Task<LocalDataClearResult> ClearMessagesAsync(CancellationToken cancellationToken = default)
        => ClearAsync(
            LocalDataCategory.Messages,
            "maintenance.clear-messages",
            ClearMessageHistoryAsync,
            cancellationToken);

    public Task<LocalDataClearResult> ClearPendingOutboundAsync(CancellationToken cancellationToken = default)
        => ClearAsync(
            LocalDataCategory.PendingOutbound,
            "maintenance.clear-pending-outbound",
            async (context, token) =>
            {
                List<Guid> openOutboxIds = await LoadOpenOutboxIdsAsync(context, token).ConfigureAwait(false);
                int packets = await DeletePacketBatchesAsync(
                    context,
                    context.Packets.Where(packet => packet.IsOutbound
                        && packet.Message != null
                        && (packet.Message.DeliveryStatus == MessageDeliveryStatus.Pending
                            || packet.Message.DeliveryStatus == MessageDeliveryStatus.Queued
                            || (packet.Message.LogicalId != null
                                && openOutboxIds.Contains(packet.Message.LogicalId.Value)))),
                    token).ConfigureAwait(false);
                int outbox = await DeleteOutboxBatchesAsync(context, token).ConfigureAwait(false);
                return packets + outbox;
            },
            cancellationToken);

    public Task<LocalDataClearResult> ClearOfflineMapTilesAsync(CancellationToken cancellationToken = default)
        => ClearAsync(
            LocalDataCategory.OfflineMapTiles,
            "maintenance.clear-offline-map-tiles",

            // The tile cache is disposable state with a small fixed row shape, so
            // one statement is enough; it never carries operator messages or work.
            (context, token) => context.MapTiles.ExecuteDeleteAsync(token),
            cancellationToken);

    public LocalDataClearResult ClearDiagnostics()
    {
        int removed = diagnostics.Events.Count;
        diagnostics.Clear();
        LogRemoved(LocalDataCategory.Diagnostics, removed);
        return new LocalDataClearResult(LocalDataCategory.Diagnostics, removed);
    }

    private async Task<LocalDataClearResult> ClearAsync(
        LocalDataCategory category,
        string operation,
        Func<AppDbContext, CancellationToken, Task<int>> clear,
        CancellationToken cancellationToken)
    {
        int removed = await writer.EnqueueAsync(operation, clear, cancellationToken).ConfigureAwait(false);
        LogRemoved(category, removed);
        return new LocalDataClearResult(category, removed);
    }

    private void LogRemoved(LocalDataCategory category, int removed) =>
        logger.LogInformation("Cleared {Category}: {Count} rows.", category, removed);

    /// <summary>
    /// Removes message history while keeping the rows that represent open durable
    /// outbound work, so clearing history can never silently drop a message that
    /// is still waiting to be delivered.
    /// </summary>
    private static async Task<int> ClearMessageHistoryAsync(AppDbContext context, CancellationToken cancellationToken)
    {
        List<Guid> openOutboxIds = await LoadOpenOutboxIdsAsync(context, cancellationToken).ConfigureAwait(false);

        return await DeletePacketBatchesAsync(
            context,
            context.Packets
                .Where(packet => packet.PacketType == PacketTypes.Message
                    || packet.PacketType == PacketTypes.MessageAck
                    || packet.PacketType == PacketTypes.MessageRej)
                .Where(packet => !packet.IsOutbound
                    || packet.Message == null
                    || (packet.Message.DeliveryStatus != MessageDeliveryStatus.Pending
                        && packet.Message.DeliveryStatus != MessageDeliveryStatus.Queued
                        && (packet.Message.LogicalId == null
                            || !openOutboxIds.Contains(packet.Message.LogicalId.Value)))),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The ids of durable outbound messages that are still open. The outbox is
    /// authoritative for delivery state, so a history row whose own status is
    /// stale is still protected while its outbox row is open.
    /// </summary>
    private static async Task<List<Guid>> LoadOpenOutboxIdsAsync(
        AppDbContext context,
        CancellationToken cancellationToken) =>
        await context.OutboxMessages
            .Where(IsOpenOutboxWork)
            .Select(message => message.Id)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

    private static async Task<int> DeletePacketBatchesAsync(
        AppDbContext context,
        IQueryable<PacketRecord> candidates,
        CancellationToken cancellationToken)
    {
        int removed = 0;
        while (true)
        {
            List<long> ids = await candidates
                .OrderBy(packet => packet.Id)
                .Select(packet => packet.Id)
                .Take(ClearBatchSize)
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            if (ids.Count == 0)
            {
                return removed;
            }

            removed += await context.Packets
                .Where(packet => ids.Contains(packet.Id))
                .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);

            if (ids.Count < ClearBatchSize)
            {
                return removed;
            }
        }
    }

    private static async Task<int> DeleteOutboxBatchesAsync(AppDbContext context, CancellationToken cancellationToken)
    {
        int removed = 0;
        while (true)
        {
            List<Guid> ids = await context.OutboxMessages
                .Where(IsOpenOutboxWork)
                .OrderBy(message => message.CreatedAt)
                .Select(message => message.Id)
                .Take(ClearBatchSize)
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            if (ids.Count == 0)
            {
                return removed;
            }

            removed += await context.OutboxMessages
                .Where(message => ids.Contains(message.Id))
                .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);

            if (ids.Count < ClearBatchSize)
            {
                return removed;
            }
        }
    }
}
