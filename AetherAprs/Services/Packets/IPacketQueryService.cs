// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AetherAprs.Data;
using AetherAprs.Data.Entities;

namespace AetherAprs.Services.Packets;

/// <summary>
/// Service for querying packet data from the database.
/// This is the primary read interface for all packet queries.
/// </summary>
public interface IPacketQueryService
{
    /// <summary>
    /// Gets the most recent packet per source callsign.
    /// </summary>
    /// <param name="limit">Maximum number of distinct sources to return.</param>
    /// <returns>Dictionary of source callsign to most recent packet record.</returns>
    Task<IReadOnlyDictionary<string, PacketRecord>> GetMostRecentPacketsAsync(int limit = 1000, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the most recent position packet per source callsign.
    /// </summary>
    /// <param name="limit">Maximum number of distinct sources to return.</param>
    /// <returns>Dictionary of source callsign to most recent position packet record.</returns>
    Task<IReadOnlyDictionary<string, PacketRecord>> GetMostRecentPositionPacketsAsync(int limit = 1000, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets packets from a specific port.
    /// </summary>
    /// <param name="portId">The port ID to filter by.</param>
    /// <param name="limit">Maximum number of packets to retrieve. Defaults to a bounded map history.</param>
    /// <returns>List of packet records from the specified port.</returns>
    Task<IReadOnlyList<PacketRecord>> GetPacketsByPortAsync(Guid portId, int limit = 500, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves historical packets for a specific callsign, ordered by most recent first.
    /// </summary>
    /// <param name="callsign">The source callsign to query.</param>
    /// <param name="limit">Maximum number of packets to retrieve.</param>
    /// <returns>A list of packet records ordered by most recent first.</returns>
    Task<IReadOnlyList<PacketRecord>> GetPacketsByCallsignAsync(string callsign, int limit = 500, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves position packets for trail display in a single bulk query,
    /// ordered newest first. Callers group the results by source callsign.
    /// </summary>
    /// <param name="cutoffTime">Optional cutoff; only packets received at or after this time are returned.</param>
    /// <param name="limit">Maximum number of packets to retrieve.</param>
    /// <returns>A list of position packet records ordered by most recent first.</returns>
    Task<IReadOnlyList<PacketRecord>> GetPositionTrailPacketsAsync(DateTimeOffset? cutoffTime = null, int limit = 5000, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the most recent object packet per object name.
    /// </summary>
    /// <param name="limit">Maximum number of distinct object names to return.</param>
    /// <returns>List of object packet records ordered by most recent first.</returns>
    Task<IReadOnlyList<PacketRecord>> GetMostRecentObjectsAsync(int limit = 1000, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the most recent item packet per item name.
    /// </summary>
    /// <param name="limit">Maximum number of distinct item names to return.</param>
    /// <returns>List of item packet records ordered by most recent first.</returns>
    Task<IReadOnlyList<PacketRecord>> GetMostRecentItemsAsync(int limit = 1000, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves historical packets for a specific object or item name, ordered by
    /// most recent first. Matches both object and item reports by name.
    /// </summary>
    /// <param name="name">The object or item name to query.</param>
    /// <param name="limit">Maximum number of packets to retrieve.</param>
    /// <returns>A list of packet records ordered by most recent first.</returns>
    Task<IReadOnlyList<PacketRecord>> GetPacketsByObjectNameAsync(string name, int limit = 500, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the most recent bulletin packets, ordered newest first.
    /// </summary>
    /// <param name="limit">Maximum number of bulletins to return.</param>
    /// <returns>List of bulletin packet records ordered by most recent first.</returns>
    Task<IReadOnlyList<PacketRecord>> GetBulletinsAsync(int limit = 500, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets recent message packets and all pending outbound messages, newest first.
    /// </summary>
    Task<IReadOnlyList<PacketRecord>> GetMessagePacketsAsync(int limit = 500, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the highest retained outbound message number, including rows outside recent history.
    /// </summary>
    Task<int?> GetHighestOutboundMessageNumberAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Searches for stations (by source callsign) and objects (by object name),
    /// matching the query as a prefix or exact match. Returns the most recent
    /// packet for each distinct match, newest first.
    /// </summary>
    /// <param name="query">The search text. Empty or whitespace returns no results.</param>
    /// <param name="limit">Maximum number of distinct matches to return.</param>
    /// <returns>List of matching packet records ordered by most recent first.</returns>
    Task<IReadOnlyList<PacketRecord>> SearchStationsAsync(string query, int limit = 100, CancellationToken cancellationToken = default);
}
