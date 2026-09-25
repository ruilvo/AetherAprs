// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Data;
using AetherAprs.Models.Aprs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace AetherAprs.Services;

/// <summary>
/// In-memory cache of recently received APRS packets.
/// Subscribes to PortService and maintains a rolling window of packets.
/// Also provides read access to historical packet data from the database.
/// </summary>
public sealed class PacketCacheService : IPacketCacheService, IDisposable
{
    private readonly IPortService _portService;
    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;
    private readonly ILogger<PacketCacheService> _logger;
    private readonly Dictionary<string, CachedPacket> _packetsBySource = new();
    private readonly object _lock = new();
    private bool _disposed;

    public event EventHandler<PacketCacheUpdatedEventArgs>? CacheUpdated;

    public PacketCacheService(
        IPortService portService,
        IDbContextFactory<AppDbContext> dbContextFactory,
        ILogger<PacketCacheService> logger)
    {
        _portService = portService ?? throw new ArgumentNullException(nameof(portService));
        _dbContextFactory = dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _portService.PacketReceived += OnPacketReceived;
        
        // Load historical packets from database on startup
        _ = LoadFromDatabaseAsync();
    }

    private async Task LoadFromDatabaseAsync()
    {
        try
        {
            await using var context = await _dbContextFactory.CreateDbContextAsync();
            
            // Load the most recent packet per source from the database
            // This populates the cache with historical data
            var recentPackets = await context.Packets
                .AsNoTracking()
                .GroupBy(p => p.Source)
                .Select(g => g.OrderByDescending(p => p.ReceivedAt).First())
                .ToListAsync();

            _logger.LogInformation("Loading {Count} historical packets into cache", recentPackets.Count);
            
            lock (_lock)
            {
                foreach (var record in recentPackets)
                {
                    var packet = PacketRecordMapper.MapToPacket(record);
                    if (packet != null)
                    {
                        var cachedPacket = new CachedPacket
                        {
                            Packet = packet,
                            PortId = record.PortId ?? Guid.Empty,
                            ReceivedAt = new DateTimeOffset(record.ReceivedAt, TimeSpan.Zero),
                            Source = record.Source
                        };
                        _packetsBySource[record.Source] = cachedPacket;
                    }
                }
            }

            _logger.LogInformation("Cache initialized with {Count} packets from database", _packetsBySource.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load historical packets from database");
        }
    }

    private void OnPacketReceived(object? sender, PortPacketReceivedEventArgs e)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var source = e.Packet.Source.ToString();
        var cachedPacket = new CachedPacket
        {
            Packet = e.Packet,
            PortId = e.PortId,
            ReceivedAt = DateTimeOffset.UtcNow,
            Source = source
        };

        lock (_lock)
        {
            _packetsBySource[source] = cachedPacket;
        }

        _logger.LogDebug(
            "Cached packet from {Source}, Type={PacketType}, Total cached: {Count}",
            source,
            e.Packet.GetType().Name,
            _packetsBySource.Count);

        // Notify subscribers
        CacheUpdated?.Invoke(this, new PacketCacheUpdatedEventArgs
        {
            UpdatedPacket = cachedPacket
        });
    }

    public IReadOnlyDictionary<string, CachedPacket> GetAllPackets()
    {
        lock (_lock)
        {
            return new Dictionary<string, CachedPacket>(_packetsBySource);
        }
    }

    public IReadOnlyDictionary<string, CachedPacket> GetPositionPackets()
    {
        lock (_lock)
        {
            return _packetsBySource
                .Where(kvp => kvp.Value.Packet is PositionPacket)
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        }
    }

    public IReadOnlyList<CachedPacket> GetPacketsFromPort(Guid portId)
    {
        lock (_lock)
        {
            return _packetsBySource.Values
                .Where(cp => cp.PortId == portId)
                .ToList();
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
        _portService.PacketReceived -= OnPacketReceived;

        lock (_lock)
        {
            _packetsBySource.Clear();
        }
    }
}
