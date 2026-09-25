// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Models.Aprs;
using System;
using System.Collections.Generic;

namespace AetherAprs.Services;

/// <summary>
/// Service that maintains an in-memory cache of recently received APRS packets.
/// Provides a shared backend for both map and packet list views.
/// </summary>
public interface IPacketCacheService
{
    /// <summary>
    /// Gets all cached packets, grouped by source callsign (most recent per source).
    /// </summary>
    IReadOnlyDictionary<string, CachedPacket> GetAllPackets();

    /// <summary>
    /// Gets position packets only, grouped by source callsign (most recent per source).
    /// </summary>
    IReadOnlyDictionary<string, CachedPacket> GetPositionPackets();

    /// <summary>
    /// Gets packets from a specific port.
    /// </summary>
    IReadOnlyList<CachedPacket> GetPacketsFromPort(Guid portId);

    /// <summary>
    /// Raised when a new packet is added or updated in the cache.
    /// </summary>
    event EventHandler<PacketCacheUpdatedEventArgs>? CacheUpdated;
}

/// <summary>
/// Represents a cached packet with metadata.
/// </summary>
public sealed class CachedPacket
{
    public required AprsPacket Packet { get; init; }
    public required Guid PortId { get; init; }
    public required DateTimeOffset ReceivedAt { get; init; }
    public required string Source { get; init; }
}

/// <summary>
/// Event args for packet cache updates.
/// </summary>
public sealed class PacketCacheUpdatedEventArgs : EventArgs
{
    public required CachedPacket UpdatedPacket { get; init; }
}
