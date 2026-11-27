// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Data.Entities;
using AetherAprs.Services.Ports;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AetherAprs.Data.Ports;

/// <summary>
/// Durable session runtime state behind the serialized runtime writer and the
/// read-only query reader.
/// </summary>
/// <remarks>
/// The snapshot is replaced as a whole: the singleton row carries the lifecycle
/// state and one row per port carries its diagnostic status. A port row that is no
/// longer part of the snapshot is removed, so a deleted port does not linger in
/// diagnostics.
/// </remarks>
internal sealed class SessionStateStore(SqliteRuntimeWriter writer, SqliteRuntimeReader reader)
    : ISessionStateStore
{
    public Task<SessionSnapshot?> LoadAsync(CancellationToken cancellationToken) =>
        reader.QueryAsync<SessionSnapshot?>(async (context, token) =>
        {
            SessionStateRecord? state = await context.SessionState.AsNoTracking()
                .FirstOrDefaultAsync(token).ConfigureAwait(false);
            if (state is null)
            {
                // No row means the database has never recorded a session; a
                // snapshot with no ports is not the same as "unknown".
                return null;
            }

            List<PortSessionStateRecord> ports = await context.PortSessionStates.AsNoTracking()
                .OrderBy(port => port.PortId)
                .ToListAsync(token).ConfigureAwait(false);

            return new SessionSnapshot(
                state.State,
                [.. ports.Select(ToStatus)]);
        }, cancellationToken);

    public Task SaveAsync(SessionSnapshot snapshot, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return writer.EnqueueAsync("session.save", async (context, token) =>
        {
            SessionStateRecord? state = await context.SessionState
                .FindAsync([SessionStateRecord.SingletonId], token).ConfigureAwait(false);
            if (state is null)
            {
                context.SessionState.Add(new SessionStateRecord { State = snapshot.State });
            }
            else
            {
                state.State = snapshot.State;
            }

            List<PortSessionStateRecord> existing = await context.PortSessionStates
                .ToListAsync(token).ConfigureAwait(false);
            Dictionary<Guid, PortSessionStateRecord> byId = [with(existing.Count)];
            foreach (PortSessionStateRecord record in existing)
            {
                byId[record.PortId] = record;
            }

            HashSet<Guid> retained = [];
            foreach (PortSessionStatus status in snapshot.Ports)
            {
                retained.Add(status.PortId);
                if (byId.TryGetValue(status.PortId, out PortSessionStateRecord? record))
                {
                    CopyTo(record, status);
                }
                else
                {
                    context.PortSessionStates.Add(ToRecord(status));
                }
            }

            PortSessionStateRecord[] removed = [.. existing.Where(record => !retained.Contains(record.PortId))];
            if (removed.Length > 0)
            {
                context.PortSessionStates.RemoveRange(removed);
            }

            await context.SaveChangesAsync(token).ConfigureAwait(false);
            return true;
        }, cancellationToken);
    }

    private static PortSessionStatus ToStatus(PortSessionStateRecord record) => new(
        record.PortId,
        record.State,
        ToUtcOffset(record.LastReceiveUtc),
        ToUtcOffset(record.LastTransmitUtc),
        record.RetryCount,
        ToUtcOffset(record.NextRetryUtc),
        record.Error);

    private static PortSessionStateRecord ToRecord(PortSessionStatus status)
    {
        PortSessionStateRecord record = new() { PortId = status.PortId };
        CopyTo(record, status);
        return record;
    }

    private static void CopyTo(PortSessionStateRecord record, PortSessionStatus status)
    {
        record.State = status.State;
        record.LastReceiveUtc = status.LastReceiveUtc?.UtcDateTime;
        record.LastTransmitUtc = status.LastTransmitUtc?.UtcDateTime;
        record.RetryCount = status.RetryCount;
        record.NextRetryUtc = status.NextRetryUtc?.UtcDateTime;
        record.Error = status.Error;
    }

    private static DateTimeOffset? ToUtcOffset(DateTime? value) =>
        value.HasValue ? new DateTimeOffset(value.Value, TimeSpan.Zero) : null;
}
