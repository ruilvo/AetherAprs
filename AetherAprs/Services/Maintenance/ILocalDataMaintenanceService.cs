// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Threading;
using System.Threading.Tasks;

namespace AetherAprs.Services.Maintenance;

/// <summary>
/// The independent categories of locally stored data an operator may clear.
/// </summary>
/// <remarks>
/// Each category owns a disjoint set of rows so a clear never removes data that
/// belongs to another category. Pending outbound messages are the only shared
/// case: their packet-history rows are protected until the durable work is
/// cleared or resolved.
/// </remarks>
public enum LocalDataCategory
{
    /// <summary>Stored packet history, excluding message packets.</summary>
    PacketHistory,

    /// <summary>Stored message history, excluding pending outbound work.</summary>
    Messages,

    /// <summary>Durable outbound message work that is still open.</summary>
    PendingOutbound,

    /// <summary>Cached offline map tiles (offline packages).</summary>
    OfflineMapTiles,

    /// <summary>The bounded in-memory diagnostic event log.</summary>
    Diagnostics
}

/// <summary>
/// The outcome of one clear action.
/// </summary>
/// <param name="Category">The category that was cleared.</param>
/// <param name="RemovedRows">How many rows or events the clear removed.</param>
public sealed record LocalDataClearResult(LocalDataCategory Category, int RemovedRows);

/// <summary>
/// Clears one category of local data at a time so an operator can delete packet
/// history, messages, pending outbound work, offline packages or diagnostics
/// independently.
/// </summary>
/// <remarks>
/// Confirmation is a presentation concern: callers ask the operator and then call
/// the matching method. The returned result carries the row count that belongs in
/// the confirmation feedback.
/// </remarks>
public interface ILocalDataMaintenanceService
{
    /// <summary>Removes stored packet history except message packets.</summary>
    /// <param name="cancellationToken">Cancels the clear.</param>
    /// <returns>How many packet rows were removed.</returns>
    Task<LocalDataClearResult> ClearPacketHistoryAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes stored message history. Pending outbound message rows stay until
    /// their durable work is cleared or resolved.
    /// </summary>
    /// <param name="cancellationToken">Cancels the clear.</param>
    /// <returns>How many packet rows were removed.</returns>
    Task<LocalDataClearResult> ClearMessagesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes open durable outbound work together with the packet-history rows
    /// that represent it, so a cleared message is never retried or left shown as
    /// pending.
    /// </summary>
    /// <param name="cancellationToken">Cancels the clear.</param>
    /// <returns>How many packet and outbox rows were removed.</returns>
    Task<LocalDataClearResult> ClearPendingOutboundAsync(CancellationToken cancellationToken = default);

    /// <summary>Removes every cached offline map tile.</summary>
    /// <param name="cancellationToken">Cancels the clear.</param>
    /// <returns>How many tile rows were removed.</returns>
    Task<LocalDataClearResult> ClearOfflineMapTilesAsync(CancellationToken cancellationToken = default);

    /// <summary>Removes the bounded diagnostic event log.</summary>
    /// <returns>How many diagnostic events were removed.</returns>
    LocalDataClearResult ClearDiagnostics();
}
