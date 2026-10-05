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
    private bool _disposed;

    public async Task<IReadOnlyDictionary<string, PacketRecord>> GetMostRecentPacketsAsync(int limit = 1000)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        try
        {
            await using var context = await dbContextFactory.CreateDbContextAsync();

            // Group by source callsign (base + SSID) and get the most recent packet per source
            var packets = await context.Packets
                .GroupBy(p => new { p.SourceBase, p.SourceSsid })
                .Select(g => g.OrderByDescending(p => p.Id).First())
                .Take(limit)
                .ToListAsync();

            // Create dictionary with "BASE-SSID" format keys
            var result = packets.ToDictionary(
                p => p.SourceSsid == AprsSsid.PrimaryStation
                    ? p.SourceBase
                    : $"{p.SourceBase}-{(int)p.SourceSsid}",
                p => p);

            logger.LogDebug("Retrieved {Count} most recent packets", result.Count);
            return result;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to retrieve most recent packets");
            throw;
        }
    }

    public async Task<IReadOnlyDictionary<string, PacketRecord>> GetMostRecentPositionPacketsAsync(int limit = 1000)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        try
        {
            await using var context = await dbContextFactory.CreateDbContextAsync();

            // Group by source callsign and get the most recent position packet per source
            var packets = await context.Packets
                .Where(p => p.Position != null)
                .GroupBy(p => new { p.SourceBase, p.SourceSsid })
                .Select(g => g.OrderByDescending(p => p.Id).First())
                .Take(limit)
                .ToListAsync();

            // Create dictionary with "BASE-SSID" format keys
            var result = packets.ToDictionary(
                p => p.SourceSsid == AprsSsid.PrimaryStation
                    ? p.SourceBase
                    : $"{p.SourceBase}-{(int)p.SourceSsid}",
                p => p);

            logger.LogDebug("Retrieved {Count} most recent position packets", result.Count);
            return result;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to retrieve most recent position packets");
            throw;
        }
    }

    public async Task<IReadOnlyList<PacketRecord>> GetPacketsByPortAsync(Guid portId, int limit = 500)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        try
        {
            await using var context = await dbContextFactory.CreateDbContextAsync();

            var packets = await context.Packets
                .Where(p => p.PortId == portId)
                .OrderByDescending(p => p.Id)
                .Take(limit)
                .ToListAsync();

            logger.LogDebug("Retrieved {Count} packets for port {PortId}", packets.Count, portId);
            return packets;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to retrieve packets for port {PortId}", portId);
            throw;
        }
    }

    public async Task<IReadOnlyList<PacketRecord>> GetPacketsByCallsignAsync(string callsign, int limit = 500)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        try
        {
            await using var context = await dbContextFactory.CreateDbContextAsync();

            // Parse callsign to extract base and SSID
            string baseCallsign;
            int? ssid = null;

            var dashIndex = callsign.IndexOf('-');
            if (dashIndex > 0 && dashIndex < callsign.Length - 1)
            {
                baseCallsign = callsign.Substring(0, dashIndex);
                if (int.TryParse(callsign.Substring(dashIndex + 1), out var parsedSsid) && parsedSsid >= 0 && parsedSsid <= 15)
                {
                    ssid = parsedSsid;
                }
                else
                {
                    baseCallsign = callsign; // Invalid SSID format, treat as base
                }
            }
            else
            {
                baseCallsign = callsign;
            }

            // Query by base and optionally SSID
            IQueryable<PacketRecord> query = context.Packets
                .Where(p => p.SourceBase == baseCallsign);

            var sourceSsid = ssid.HasValue
                ? (AprsSsid)ssid.Value
                : AprsSsid.PrimaryStation;

            query = query.Where(p => p.SourceSsid == sourceSsid);

            var packets = await query
                .OrderByDescending(p => p.Id)
                .Take(limit)
                .ToListAsync();

            logger.LogDebug("Retrieved {Count} historical packets for callsign {Callsign}", packets.Count, callsign);

            return packets;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to retrieve packets for callsign {Callsign}", callsign);
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
