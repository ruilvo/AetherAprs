// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Models.Aprs;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AetherAprs.Services;

/// <summary>
/// In-memory cache of recently received APRS packets.
/// Subscribes to PortService and maintains a rolling window of packets.
/// </summary>
public sealed class PacketCacheService : IPacketCacheService, IDisposable
{
    private readonly IPortService _portService;
    private readonly ILogger<PacketCacheService> _logger;
    private readonly Dictionary<string, CachedPacket> _packetsBySource = new();
    private readonly object _lock = new();
    private bool _disposed;

    public event EventHandler<PacketCacheUpdatedEventArgs>? CacheUpdated;

    public PacketCacheService(
        IPortService portService,
        ILogger<PacketCacheService> logger)
    {
        _portService = portService ?? throw new ArgumentNullException(nameof(portService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _portService.PacketReceived += OnPacketReceived;
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
