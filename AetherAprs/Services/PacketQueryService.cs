// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AetherAprs.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AetherAprs.Services;

/// <summary>
/// Service for querying packet data from the database.
/// This service provides read-only access to packet data for all consumer features.
/// </summary>
public sealed class PacketQueryService : IPacketQueryService, IDisposable
{
    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;
    private readonly ILogger<PacketQueryService> _logger;
    private bool _disposed;

    public PacketQueryService(
        IDbContextFactory<AppDbContext> dbContextFactory,
        ILogger<PacketQueryService> logger)
    {
        _dbContextFactory = dbContextFactory;
        _logger = logger;
    }

    public async Task<IReadOnlyDictionary<string, PacketRecord>> GetMostRecentPacketsAsync(int limit = 1000)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        try
        {
            await using var context = await _dbContextFactory.CreateDbContextAsync();

            // Group by source and get the most recent packet per source
            var packets = await context.Packets
                .GroupBy(p => p.Source)
                .Select(g => g.OrderByDescending(p => p.Id).First())
                .Take(limit)
                .ToDictionaryAsync(p => p.Source, p => p);

            _logger.LogDebug("Retrieved {Count} most recent packets", packets.Count);
            return packets;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve most recent packets");
            throw;
        }
    }

    public async Task<IReadOnlyDictionary<string, PacketRecord>> GetMostRecentPositionPacketsAsync(int limit = 1000)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        try
        {
            await using var context = await _dbContextFactory.CreateDbContextAsync();

            // Group by source and get the most recent position packet per source
            var packets = await context.Packets
                .Where(p => p.Latitude.HasValue && p.Longitude.HasValue)
                .GroupBy(p => p.Source)
                .Select(g => g.OrderByDescending(p => p.Id).First())
                .Take(limit)
                .ToDictionaryAsync(p => p.Source, p => p);

            _logger.LogDebug("Retrieved {Count} most recent position packets", packets.Count);
            return packets;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve most recent position packets");
            throw;
        }
    }

    public async Task<IReadOnlyList<PacketRecord>> GetPacketsByPortAsync(Guid portId, int limit = 500)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        try
        {
            await using var context = await _dbContextFactory.CreateDbContextAsync();

            var packets = await context.Packets
                .Where(p => p.PortId == portId)
                .OrderByDescending(p => p.Id)
                .Take(limit)
                .ToListAsync();

            _logger.LogDebug("Retrieved {Count} packets for port {PortId}", packets.Count, portId);
            return packets;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve packets for port {PortId}", portId);
            throw;
        }
    }

    public async Task<IReadOnlyList<PacketRecord>> GetPacketsByCallsignAsync(string callsign, int limit = 500)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        try
        {
            await using var context = await _dbContextFactory.CreateDbContextAsync();

            // Order by Id descending (auto-increment primary key) instead of DateTimeOffset
            // This works because SQLite auto-increment ensures newer packets have higher IDs
            var packets = await context.Packets
                .Where(p => p.Source == callsign)
                .OrderByDescending(p => p.Id)
                .Take(limit)
                .ToListAsync();

            _logger.LogDebug("Retrieved {Count} historical packets for callsign {Callsign}", packets.Count, callsign);

            return packets;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve packets for callsign {Callsign}", callsign);
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
