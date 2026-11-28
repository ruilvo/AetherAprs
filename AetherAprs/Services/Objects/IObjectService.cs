// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using AetherAprs.Models.Aprs;
using AetherAprs.Models.Aprs.Packets;

using Geo;

namespace AetherAprs.Services.Objects;

/// <summary>
/// Service for creating, updating, killing, and listing APRS objects.
/// Objects are transmitted via <see cref="Ports.IPortService"/> and persisted
/// through <see cref="Packets.IPacketStorageService"/>, mirroring the message flow.
/// </summary>
public interface IObjectService
{
    /// <summary>
    /// Creates or updates a live object with the given name, position, and symbol.
    /// Re-transmitting an existing name updates it on the air and in the database.
    /// </summary>
    /// <param name="name">Object name (up to 9 characters).</param>
    /// <param name="location">Geographic coordinate.</param>
    /// <param name="symbolTable">APRS symbol table.</param>
    /// <param name="symbolCode">APRS symbol code.</param>
    /// <param name="comment">Optional comment text.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task CreateOrUpdateAsync(
        string name,
        Coordinate location,
        SymbolTable symbolTable,
        SymbolCode symbolCode,
        string? comment = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Kills an existing object by re-transmitting it with the killed flag and its
    /// last known position. The killed object remains in the database so the list
    /// can show it as killed.
    /// </summary>
    /// <param name="name">Object name to kill.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="InvalidOperationException">Thrown when no object with the given name is known.</exception>
    Task KillAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the most recent object packet per object name, newest first.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Object packets (live and killed) ordered by most recent first.</returns>
    Task<IReadOnlyList<ObjectPacket>> GetObjectsAsync(CancellationToken cancellationToken = default);
}
