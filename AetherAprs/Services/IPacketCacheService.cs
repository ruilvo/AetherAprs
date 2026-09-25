// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Data;
using AetherAprs.Models.Aprs;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AetherAprs.Services;

/// <summary>
/// Service that maintains an in-memory cache of recently received APRS packets
/// and provides read access to historical packet data.
/// This is the single read interface for all packet queries.
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
    /// Gets historical packets from a specific callsign from the database.
    /// Results are ordered by most recent first.
    /// </summary>
    /// <param name="callsign">The source callsign to query.</param>
    /// <param name="limit">Maximum number of packets to return (default: 500).</param>
    /// <returns>List of packet records ordered by ReceivedAt descending.</returns>
    Task<IReadOnlyList<PacketRecord>> GetPacketsByCallsignAsync(string callsign, int limit = 500);

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
