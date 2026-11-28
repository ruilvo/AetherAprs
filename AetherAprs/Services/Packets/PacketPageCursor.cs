// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;

using AetherAprs.Data.Entities;

namespace AetherAprs.Services.Packets;

/// <summary>
/// Stable paging cursor for packet history. Pages are ordered by receipt time and
/// then by row id, so the boundary between two pages is the
/// <see cref="Timestamp"/> and <see cref="Id"/> pair of the last row already shown.
/// Using both keys keeps every row reachable when several packets share a
/// timestamp, which a bare id cursor could not guarantee once rows are written
/// out of timestamp order.
/// </summary>
/// <param name="Id">Row id of the last packet already shown.</param>
/// <param name="Timestamp">Receipt time (UTC) of the last packet already shown.</param>
public readonly record struct PacketPageCursor(long Id, DateTimeOffset Timestamp)
{
    /// <summary>
    /// Builds the cursor that continues after <paramref name="packet"/>.
    /// </summary>
    /// <param name="packet">A packet record returned by a page query.</param>
    public static PacketPageCursor FromPacket(PacketRecord packet)
    {
        ArgumentNullException.ThrowIfNull(packet);

        // SQLite round-trips DateTime without a Kind, and the data layer stores
        // UTC exclusively, so an Unspecified value is UTC rather than local time.
        DateTime timestamp = packet.Timestamp;
        DateTime utc = timestamp.Kind switch
        {
            DateTimeKind.Utc => timestamp,
            DateTimeKind.Local => timestamp.ToUniversalTime(),
            _ => DateTime.SpecifyKind(timestamp, DateTimeKind.Utc)
        };

        return new PacketPageCursor(packet.Id, new DateTimeOffset(utc, TimeSpan.Zero));
    }
}
