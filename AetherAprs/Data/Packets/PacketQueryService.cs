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
            // Use max IDs in a separate query to avoid EF SQLite group-projection limitations.
            var maxIds = await context.Packets
                .GroupBy(packet => new { packet.SourceBase, packet.SourceSsid })
                .Select(group => group.Max(packet => packet.Id))
                .ToListAsync(ct).ConfigureAwait(false);

            var packets = await GetLatestByIdsAsync(context, maxIds, limit, ct).ConfigureAwait(false);
            return ByCallsign(packets);
        }, cancellationToken);

    public Task<IReadOnlyDictionary<string, PacketRecord>> GetMostRecentPositionPacketsAsync(
        int limit = 1000,
        CancellationToken cancellationToken = default) =>
        ReadAsync<IReadOnlyDictionary<string, PacketRecord>>(nameof(GetMostRecentPositionPacketsAsync), async (context, ct) =>
        {
            var maxIds = await context.Packets
                .Where(packet => packet.Position != null)
                .GroupBy(packet => new { packet.SourceBase, packet.SourceSsid })
                .Select(group => group.Max(packet => packet.Id))
                .ToListAsync(ct).ConfigureAwait(false);

            var packets = await GetLatestByIdsAsync(context, maxIds, limit, ct).ConfigureAwait(false);
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
            var baseCallsign = callsign;
            AprsSsid? ssid = null;
            var dashIndex = callsign.IndexOf('-');
            if (dashIndex > 0 && dashIndex < callsign.Length - 1)
            {
                baseCallsign = callsign[..dashIndex];
                if (int.TryParse(callsign.AsSpan(dashIndex + 1), out var parsedSsid) && parsedSsid is >= MinSsid and <= MaxSsid)
                {
                    ssid = (AprsSsid)parsedSsid;
                }
                // Invalid SSIDs still fall back to matching the base callsign.
            }

            var query = context.Packets.Where(packet => packet.SourceBase == baseCallsign);
            if (ssid is { } sourceSsid)
            {
                query = query.Where(packet => packet.SourceSsid == sourceSsid);
            }

            return await GetLatestAsync(query, limit, ct).ConfigureAwait(false);
        }, cancellationToken);

    public Task<IReadOnlyList<PacketRecord>> GetPositionTrailPacketsAsync(
        DateTimeOffset? cutoffTime = null,
        int limit = 5000,
        CancellationToken cancellationToken = default) =>
        ReadAsync<IReadOnlyList<PacketRecord>>(nameof(GetPositionTrailPacketsAsync), async (context, ct) =>
        {
            var query = context.Packets.Where(packet => packet.Position != null);
            if (cutoffTime is { } cutoff)
            {
                var cutoffUtc = cutoff.UtcDateTime;
                query = query.Where(packet => packet.Timestamp >= cutoffUtc);
            }

            return await GetLatestAsync(query, limit, ct).ConfigureAwait(false);
        }, cancellationToken);

    public Task<IReadOnlyList<PacketRecord>> GetMostRecentObjectsAsync(
        int limit = 1000,
        CancellationToken cancellationToken = default) =>
        ReadAsync<IReadOnlyList<PacketRecord>>(nameof(GetMostRecentObjectsAsync), async (context, ct) =>
        {
            var maxIds = await context.Packets
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
            var maxIds = await context.Packets
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
        var trimmed = name?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return [];
        }

        return await ReadAsync<IReadOnlyList<PacketRecord>>(nameof(GetPacketsByObjectNameAsync), async (context, ct) =>
            await GetLatestAsync(context.Packets.Where(packet =>
                (packet.Object != null && packet.Object.Name == trimmed) ||
                (packet.Item != null && packet.Item.Name == trimmed)), limit, ct).ConfigureAwait(false), cancellationToken)
            .ConfigureAwait(false);
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
            var messages = context.Packets
                .Where(packet => packet.PacketType == PacketTypes.Message && packet.Message != null);
            var recentIds = messages.OrderByDescending(packet => packet.Id).Take(limit).Select(packet => packet.Id);
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
        var trimmed = query?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return [];
        }

        var normalized = trimmed.ToUpperInvariant();
        return await ReadAsync<IReadOnlyList<PacketRecord>>(nameof(SearchStationsAsync), async (context, ct) =>
        {
            var candidates = await context.Packets
                .Where(packet =>
                    packet.SourceBase.StartsWith(normalized) || packet.SourceBase == normalized ||
                    (packet.Object != null &&
                        (packet.Object.Name.StartsWith(normalized) || packet.Object.Name == normalized)))
                .OrderByDescending(packet => packet.Id)
                .Take(limit * 10)
                .ToListAsync(ct).ConfigureAwait(false);

            // The first occurrence of each match is the newest report.
            var seen = new HashSet<string>();
            var results = new List<PacketRecord>();
            foreach (var packet in candidates)
            {
                var key = packet.Object != null &&
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

    private static Task<List<PacketRecord>> GetLatestByIdsAsync(
        AppDbContext context, List<long> ids, int limit, CancellationToken cancellationToken) =>
        GetLatestAsync(context.Packets.Where(packet => ids.Contains(packet.Id)), limit, cancellationToken);

    private static Task<List<PacketRecord>> GetLatestAsync(
        IQueryable<PacketRecord> query, int limit, CancellationToken cancellationToken) =>
        query.OrderByDescending(packet => packet.Id).Take(limit).ToListAsync(cancellationToken);

    private static Dictionary<string, PacketRecord> ByCallsign(IEnumerable<PacketRecord> packets) =>
        packets.ToDictionary(packet => packet.SourceSsid == AprsSsid.PrimaryStation
            ? packet.SourceBase
            : $"{packet.SourceBase}-{(int)packet.SourceSsid}");
}
