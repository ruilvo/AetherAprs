// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Data.Entities;
using AetherAprs.Data.Mappers;
using AetherAprs.Models.Aprs;
using AetherAprs.Models.Messaging;
using AetherAprs.Services.Packets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AetherAprs.Data.Packets;

/// <summary>Materializes packet history through the runtime reader for feature consumers.</summary>
internal sealed class PacketQueryService(
    SqliteRuntimeReader reader,
    ILogger<PacketQueryService> logger) : IPacketQueryService, IDisposable
{
    private const int MinSsid = 0;
    private const int MaxSsid = 15;
    private bool _disposed;

    public Task<IReadOnlyDictionary<string, PacketRecord>> GetMostRecentPacketsAsync(
        int limit = 1000,
        CancellationToken cancellationToken = default) =>
        ReadAsync<IReadOnlyDictionary<string, PacketRecord>>(nameof(GetMostRecentPacketsAsync), async (context, ct) =>
        {
            // A correlated subquery selects the newest row per station without grouping by
            // a projected key: an anonymous-type GroupBy selector compiles to
            // Expression.New, which the trimmer cannot verify so it reports IL2026.
            List<PacketRecord> packets = await GetLatestAsync(NewestPerStation(context.Packets), limit, ct)
                .ConfigureAwait(false);
            return ByCallsign(packets);
        }, cancellationToken);

    public Task<IReadOnlyDictionary<string, PacketRecord>> GetMostRecentPositionPacketsAsync(
        int limit = 1000,
        CancellationToken cancellationToken = default) =>
        ReadAsync<IReadOnlyDictionary<string, PacketRecord>>(nameof(GetMostRecentPositionPacketsAsync), async (context, ct) =>
        {
            List<PacketRecord> packets = await GetLatestAsync(
                NewestPerStation(context.Packets.Where(packet => packet.Position != null)),
                limit,
                ct).ConfigureAwait(false);
            return ByCallsign(packets);
        }, cancellationToken);

    public Task<IReadOnlyList<PacketRecord>> GetPacketsByPortAsync(
        Guid portId,
        int limit = 500,
        CancellationToken cancellationToken = default) =>
        ReadAsync<IReadOnlyList<PacketRecord>>(nameof(GetPacketsByPortAsync), async (context, ct) =>
            await GetLatestAsync(context.Packets.Where(packet => packet.PortId == portId), limit, ct)
                .ConfigureAwait(false), cancellationToken);

    public Task<IReadOnlyList<PacketRecord>> GetPacketsByCallsignAsync(
        string callsign,
        int limit = 500,
        CancellationToken cancellationToken = default) =>
        ReadAsync<IReadOnlyList<PacketRecord>>(nameof(GetPacketsByCallsignAsync), async (context, ct) =>
        {
            IQueryable<PacketRecord> query = FilterByCallsign(context.Packets, callsign);
            return await GetLatestAsync(query, limit, ct).ConfigureAwait(false);
        }, cancellationToken);

    public Task<IReadOnlyList<PacketRecord>> GetPacketsByCallsignPageAsync(
        string callsign,
        PacketPageCursor? cursor = null,
        int pageSize = 50,
        CancellationToken cancellationToken = default) =>
        ReadAsync<IReadOnlyList<PacketRecord>>(nameof(GetPacketsByCallsignPageAsync), async (context, ct) =>
        {
            IQueryable<PacketRecord> query = FilterByCallsign(context.Packets, callsign);
            return await GetLatestPageAsync(ApplyCursor(query, cursor), pageSize, ct).ConfigureAwait(false);
        }, cancellationToken);

    public Task<IReadOnlyList<PacketRecord>> GetPositionTrailPacketsAsync(
        DateTimeOffset? cutoffTime = null,
        int limit = 5000,
        CancellationToken cancellationToken = default) =>
        ReadAsync<IReadOnlyList<PacketRecord>>(nameof(GetPositionTrailPacketsAsync), async (context, ct) =>
        {
            IQueryable<PacketRecord> query = context.Packets.Where(packet => packet.Position != null);
            if (cutoffTime is { } cutoff)
            {
                DateTime cutoffUtc = cutoff.UtcDateTime;
                query = query.Where(packet => packet.Timestamp >= cutoffUtc);
            }

            return await GetLatestAsync(query, limit, ct).ConfigureAwait(false);
        }, cancellationToken);

    public Task<IReadOnlyList<PacketRecord>> GetMostRecentObjectsAsync(
        int limit = 1000,
        CancellationToken cancellationToken = default) =>
        ReadAsync<IReadOnlyList<PacketRecord>>(nameof(GetMostRecentObjectsAsync), async (context, ct) =>
        {
            List<long> maxIds = await context.Packets
                .Where(packet => packet.Object != null)
                .GroupBy(packet => packet.Object!.Name)
                .Select(group => group.Max(packet => packet.Id))
                .ToListAsync(ct).ConfigureAwait(false);

            return await GetLatestByIdsAsync(context, maxIds, limit, ct).ConfigureAwait(false);
        }, cancellationToken);

    public Task<IReadOnlyList<PacketRecord>> GetMostRecentItemsAsync(
        int limit = 1000,
        CancellationToken cancellationToken = default) =>
        ReadAsync<IReadOnlyList<PacketRecord>>(nameof(GetMostRecentItemsAsync), async (context, ct) =>
        {
            List<long> maxIds = await context.Packets
                .Where(packet => packet.Item != null)
                .GroupBy(packet => packet.Item!.Name)
                .Select(group => group.Max(packet => packet.Id))
                .ToListAsync(ct).ConfigureAwait(false);

            return await GetLatestByIdsAsync(context, maxIds, limit, ct).ConfigureAwait(false);
        }, cancellationToken);

    public async Task<IReadOnlyList<PacketRecord>> GetPacketsByObjectNameAsync(
        string name,
        int limit = 500,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        string? trimmed = name?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return [];
        }

        return await ReadAsync<IReadOnlyList<PacketRecord>>(nameof(GetPacketsByObjectNameAsync), async (context, ct) =>
            await GetLatestAsync(FilterByObjectName(context.Packets, trimmed), limit, ct).ConfigureAwait(false), cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<PacketRecord>> GetPacketsByObjectNamePageAsync(
        string name,
        PacketPageCursor? cursor = null,
        int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        string? trimmed = name?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return [];
        }

        return await ReadAsync<IReadOnlyList<PacketRecord>>(nameof(GetPacketsByObjectNamePageAsync), async (context, ct) =>
        {
            IQueryable<PacketRecord> query = FilterByObjectName(context.Packets, trimmed);
            return await GetLatestPageAsync(ApplyCursor(query, cursor), pageSize, ct).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
    }

    public Task<IReadOnlyList<PacketRecord>> GetBulletinsAsync(
        int limit = 500,
        CancellationToken cancellationToken = default) =>
        ReadAsync<IReadOnlyList<PacketRecord>>(nameof(GetBulletinsAsync), async (context, ct) =>
            await GetLatestAsync(context.Packets.Where(packet => packet.Bulletin != null), limit, ct)
                .ConfigureAwait(false), cancellationToken);

    public Task<IReadOnlyList<PacketRecord>> GetMessagePacketsAsync(
        int limit = 500,
        CancellationToken cancellationToken = default) =>
        ReadAsync<IReadOnlyList<PacketRecord>>(nameof(GetMessagePacketsAsync), async (context, ct) =>
        {
            IQueryable<PacketRecord> messages = context.Packets
                .Where(packet => packet.PacketType == PacketTypes.Message && packet.Message != null);
            IQueryable<long> recentIds = messages.OrderByDescending(packet => packet.Id).Take(limit).Select(packet => packet.Id);
            return await messages
                .Where(packet => recentIds.Contains(packet.Id) ||
                    (packet.IsOutbound && packet.Message!.DeliveryStatus == MessageDeliveryStatus.Pending))
                .OrderByDescending(packet => packet.Id)
                .ToListAsync(ct).ConfigureAwait(false);
        }, cancellationToken);

    public Task<int?> GetHighestOutboundMessageNumberAsync(CancellationToken cancellationToken = default) =>
        ReadAsync(nameof(GetHighestOutboundMessageNumberAsync), (context, ct) =>
            context.Packets
                .Where(packet => packet.IsOutbound && packet.PacketType == PacketTypes.Message && packet.Message != null)
                .MaxAsync(packet => packet.Message!.Number, ct), cancellationToken);

    public async Task<IReadOnlyList<PacketRecord>> SearchStationsAsync(
        string query,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        string? trimmed = query?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return [];
        }

        string normalized = trimmed.ToUpperInvariant();
        return await ReadAsync<IReadOnlyList<PacketRecord>>(nameof(SearchStationsAsync), async (context, ct) =>
        {
            List<PacketRecord> candidates = await context.Packets
                .Where(packet =>
                    packet.SourceBase.StartsWith(normalized) || packet.SourceBase == normalized ||
                    (packet.Object != null &&
                        (packet.Object.Name.StartsWith(normalized) || packet.Object.Name == normalized)))
                .OrderByDescending(packet => packet.Id)
                .Take(limit * 10)
                .ToListAsync(ct).ConfigureAwait(false);

            // The first occurrence of each match is the newest report.
            HashSet<string> seen = [];
            List<PacketRecord> results = [];
            foreach (PacketRecord? packet in candidates)
            {
                string key = packet.Object != null &&
                    (packet.Object.Name.StartsWith(normalized, StringComparison.Ordinal) || packet.Object.Name == normalized)
                    ? $"obj:{packet.Object.Name}"
                    : $"{packet.SourceBase}-{(int)packet.SourceSsid}";

                if (seen.Add(key))
                {
                    results.Add(packet);
                    if (results.Count >= limit)
                    {
                        break;
                    }
                }
            }

            return results;
        }, cancellationToken).ConfigureAwait(false);
    }

    public void Dispose() => _disposed = true;

    private async Task<T> ReadAsync<T>(
        string operation,
        Func<AppDbContext, CancellationToken, Task<T>> query,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        try
        {
            return await reader.QueryAsync(query, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Packet query {Operation} failed", operation);
            throw;
        }
    }

    private static IQueryable<PacketRecord> NewestPerStation(IQueryable<PacketRecord> packets) =>
        packets.Where(packet => packet.Id == packets
            .Where(inner => inner.SourceBase == packet.SourceBase && inner.SourceSsid == packet.SourceSsid)
            .Max(inner => inner.Id));

    private static IQueryable<PacketRecord> FilterByCallsign(IQueryable<PacketRecord> packets, string callsign)
    {
        string baseCallsign = callsign;
        AprsSsid? ssid = null;
        int dashIndex = callsign.IndexOf('-');
        if (dashIndex > 0 && dashIndex < callsign.Length - 1)
        {
            baseCallsign = callsign[..dashIndex];
            if (int.TryParse(callsign.AsSpan(dashIndex + 1), out int parsedSsid) && parsedSsid is >= MinSsid and <= MaxSsid)
            {
                ssid = (AprsSsid)parsedSsid;
            }
            // Invalid SSIDs still fall back to matching the base callsign.
        }

        IQueryable<PacketRecord> query = packets.Where(packet => packet.SourceBase == baseCallsign);
        if (ssid is { } sourceSsid)
        {
            query = query.Where(packet => packet.SourceSsid == sourceSsid);
        }

        return query;
    }

    private static IQueryable<PacketRecord> FilterByObjectName(IQueryable<PacketRecord> packets, string name) =>
        packets.Where(packet =>
            (packet.Object != null && packet.Object.Name == name) ||
            (packet.Item != null && packet.Item.Name == name));

    private static Task<List<PacketRecord>> GetLatestByIdsAsync(
        AppDbContext context, List<long> ids, int limit, CancellationToken cancellationToken) =>
        GetLatestAsync(context.Packets.Where(packet => ids.Contains(packet.Id)), limit, cancellationToken);

    private static Task<List<PacketRecord>> GetLatestAsync(
        IQueryable<PacketRecord> query, int limit, CancellationToken cancellationToken) =>
        query.OrderByDescending(packet => packet.Id).Take(limit).ToListAsync(cancellationToken);

    /// <summary>
    /// Applies a stable <c>(timestamp, id)</c> cursor: rows older than the cursor
    /// timestamp, or rows that share it with a smaller id, belong to later pages.
    /// </summary>
    private static IQueryable<PacketRecord> ApplyCursor(IQueryable<PacketRecord> query, PacketPageCursor? cursor)
    {
        if (cursor is not { } pageCursor)
        {
            return query;
        }

        DateTime timestamp = pageCursor.Timestamp.UtcDateTime;
        long id = pageCursor.Id;
        return query.Where(packet =>
            packet.Timestamp < timestamp ||
            (packet.Timestamp == timestamp && packet.Id < id));
    }

    /// <summary>
    /// Reads one page ordered by receipt time and then row id. Both keys descend so
    /// the newest row is first and ties on a shared timestamp keep a total order.
    /// </summary>
    private static Task<List<PacketRecord>> GetLatestPageAsync(
        IQueryable<PacketRecord> query, int limit, CancellationToken cancellationToken) =>
        query
            .OrderByDescending(packet => packet.Timestamp)
            .ThenByDescending(packet => packet.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);

    private static Dictionary<string, PacketRecord> ByCallsign(IEnumerable<PacketRecord> packets) =>
        packets.ToDictionary(packet => packet.SourceSsid == AprsSsid.PrimaryStation
            ? packet.SourceBase
            : $"{packet.SourceBase}-{(int)packet.SourceSsid}");
}
