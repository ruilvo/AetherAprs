// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AetherAprs.Data;

namespace AetherAprs.Services;

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
    Task<IReadOnlyDictionary<string, PacketRecord>> GetMostRecentPacketsAsync(int limit = 1000);

    /// <summary>
    /// Gets the most recent position packet per source callsign.
    /// </summary>
    /// <param name="limit">Maximum number of distinct sources to return.</param>
    /// <returns>Dictionary of source callsign to most recent position packet record.</returns>
    Task<IReadOnlyDictionary<string, PacketRecord>> GetMostRecentPositionPacketsAsync(int limit = 1000);

    /// <summary>
    /// Gets packets from a specific port.
    /// </summary>
    /// <param name="portId">The port ID to filter by.</param>
    /// <param name="limit">Maximum number of packets to retrieve.</param>
    /// <returns>List of packet records from the specified port.</returns>
    Task<IReadOnlyList<PacketRecord>> GetPacketsByPortAsync(Guid portId, int limit = 500);

    /// <summary>
    /// Retrieves historical packets for a specific callsign, ordered by most recent first.
    /// </summary>
    /// <param name="callsign">The source callsign to query.</param>
    /// <param name="limit">Maximum number of packets to retrieve.</param>
    /// <returns>A list of packet records ordered by most recent first.</returns>
    Task<IReadOnlyList<PacketRecord>> GetPacketsByCallsignAsync(string callsign, int limit = 500);
}
