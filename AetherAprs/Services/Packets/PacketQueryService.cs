// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Data;
using AetherAprs.Data.Entities;
using AetherAprs.Models.Aprs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AetherAprs.Services.Packets;

/// <summary>
/// Service for querying packet data from the database.
/// This service provides read-only access to packet data for all consumer features.
/// </summary>
public sealed class PacketQueryService(
    IDbContextFactory<AppDbContext> dbContextFactory,
    ILogger<PacketQueryService> logger) : IPacketQueryService, IDisposable
{
    /// <summary>Minimum and maximum SSID values per the APRS specification.</summary>
    private const int MinSsid = 0;
    private const int MaxSsid = 15;

    private bool _disposed;

    public async Task<IReadOnlyDictionary<string, PacketRecord>> GetMostRecentPacketsAsync(
        int limit = 1000,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        try
        {
            await using var context = await dbContextFactory.CreateDbContextAsync(cancellationToken);

            // Two-step query to work around EF Core SQLite translation limitations:
            // 1. Get max ID per source callsign (base + SSID)
            var maxIds = await context.Packets
                .AsNoTracking()
                .GroupBy(p => new { p.SourceBase, p.SourceSsid })
                .Select(g => g.Max(p => p.Id))
                .ToListAsync(cancellationToken);

            // 2. Fetch those packets and order deterministically
            var packets = await context.Packets
                .AsNoTracking()
                .Where(p => maxIds.Contains(p.Id))
                .OrderByDescending(p => p.Id)
                .Take(limit)
                .ToListAsync(cancellationToken);

            // Create dictionary with "BASE-SSID" format keys
            var result = packets.ToDictionary(
                p => p.SourceSsid == AprsSsid.PrimaryStation
                    ? p.SourceBase
                    : $"{p.SourceBase}-{(int)p.SourceSsid}",
                p => p);

            logger.LogDebug("Retrieved {Count} most recent packets", result.Count);
            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to retrieve most recent packets");
            throw;
        }
    }

    public async Task<IReadOnlyDictionary<string, PacketRecord>> GetMostRecentPositionPacketsAsync(
        int limit = 1000,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        try
        {
            await using var context = await dbContextFactory.CreateDbContextAsync(cancellationToken);

            // Two-step query to work around EF Core SQLite translation limitations:
            // 1. Get max ID per source callsign (base + SSID) for position packets
            var maxIds = await context.Packets
                .AsNoTracking()
                .Where(p => p.Position != null)
                .GroupBy(p => new { p.SourceBase, p.SourceSsid })
                .Select(g => g.Max(p => p.Id))
                .ToListAsync(cancellationToken);

            // 2. Fetch those packets and order deterministically
            var packets = await context.Packets
                .AsNoTracking()
                .Where(p => maxIds.Contains(p.Id))
                .OrderByDescending(p => p.Id)
                .Take(limit)
                .ToListAsync(cancellationToken);

            // Create dictionary with "BASE-SSID" format keys
            var result = packets.ToDictionary(
                p => p.SourceSsid == AprsSsid.PrimaryStation
                    ? p.SourceBase
                    : $"{p.SourceBase}-{(int)p.SourceSsid}",
                p => p);

            logger.LogDebug("Retrieved {Count} most recent position packets", result.Count);
            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to retrieve most recent position packets");
            throw;
        }
    }

    public async Task<IReadOnlyList<PacketRecord>> GetPacketsByPortAsync(
        Guid portId,
        int limit = 500,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        try
        {
            await using var context = await dbContextFactory.CreateDbContextAsync(cancellationToken);

            var packets = await context.Packets
                .AsNoTracking()
                .Where(p => p.PortId == portId)
                .OrderByDescending(p => p.Id)
                .Take(limit)
                .ToListAsync(cancellationToken);

            logger.LogDebug("Retrieved {Count} packets for port {PortId}", packets.Count, portId);
            return packets;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to retrieve packets for port {PortId}", portId);
            throw;
        }
    }

    public async Task<IReadOnlyList<PacketRecord>> GetPacketsByCallsignAsync(
        string callsign,
        int limit = 500,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        try
        {
            await using var context = await dbContextFactory.CreateDbContextAsync(cancellationToken);

            // Parse callsign to extract base and SSID
            string baseCallsign;
            int? ssid = null;

            var dashIndex = callsign.IndexOf('-');
            if (dashIndex > 0 && dashIndex < callsign.Length - 1)
            {
                baseCallsign = callsign.Substring(0, dashIndex);
                if (int.TryParse(callsign.Substring(dashIndex + 1), out var parsedSsid)
                    && parsedSsid >= MinSsid
                    && parsedSsid <= MaxSsid)
                {
                    ssid = parsedSsid;
                }
                // Invalid SSID: keep the already-stripped baseCallsign so the
                // query falls back to matching the base callsign without an SSID filter
            }
            else
            {
                baseCallsign = callsign;
            }

            // Query by base callsign, optionally filtering by SSID
            IQueryable<PacketRecord> query = context.Packets
                .AsNoTracking()
                .Where(p => p.SourceBase == baseCallsign);

            if (ssid.HasValue)
            {
                var sourceSsid = (AprsSsid)ssid.Value;
                query = query.Where(p => p.SourceSsid == sourceSsid);
            }

            var packets = await query
                .OrderByDescending(p => p.Id)
                .Take(limit)
                .ToListAsync(cancellationToken);

            logger.LogDebug("Retrieved {Count} historical packets for callsign {Callsign}", packets.Count, callsign);

            return packets;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to retrieve packets for callsign {Callsign}", callsign);
            throw;
        }
    }

    public async Task<IReadOnlyList<PacketRecord>> GetPositionTrailPacketsAsync(
        DateTimeOffset? cutoffTime = null,
        int limit = 50000,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        try
        {
            await using var context = await dbContextFactory.CreateDbContextAsync(cancellationToken);

            var query = context.Packets
                .AsNoTracking()
                .Where(p => p.Position != null);

            if (cutoffTime.HasValue)
            {
                var cutoffUtc = cutoffTime.Value.UtcDateTime;
                query = query.Where(p => p.Timestamp >= cutoffUtc);
            }

            var packets = await query
                .OrderByDescending(p => p.Id)
                .Take(limit)
                .ToListAsync(cancellationToken);

            logger.LogDebug("Retrieved {Count} position trail packets", packets.Count);
            return packets;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to retrieve position trail packets");
            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
    }
}
