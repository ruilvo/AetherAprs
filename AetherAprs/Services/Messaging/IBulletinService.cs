// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using AetherAprs.Models.Aprs.Packets;

namespace AetherAprs.Services.Messaging;

/// <summary>
/// Service for sending and listing APRS bulletins. Bulletins are broadcast
/// messages addressed to a <c>BLN</c>-prefixed name rather than an individual
/// callsign, so this service takes a name string rather than a <see cref="AetherAprs.Models.Aprs.Callsign"/>.
/// </summary>
public interface IBulletinService
{
    /// <summary>
    /// Sends a bulletin with the given name and text on all enabled TX ports,
    /// persisting it as an outbound packet.
    /// </summary>
    /// <param name="name">Bulletin name (must start with <c>BLN</c>, e.g. <c>BLN0</c> or <c>BLN0SPCL</c>).</param>
    /// <param name="text">Bulletin text (up to 67 characters).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="ArgumentException">Thrown when the name or text is invalid.</exception>
    Task SendAsync(string name, string text, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the most recent bulletins, newest first.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Bulletin packets ordered by most recent first.</returns>
    Task<IReadOnlyList<BulletinPacket>> GetBulletinsAsync(CancellationToken cancellationToken = default);
}
