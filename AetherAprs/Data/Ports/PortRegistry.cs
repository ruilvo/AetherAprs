// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Configuration;
using AetherAprs.Data.Entities;
using AetherAprs.Data.Mappers;
using AetherAprs.Services.Platform;
using AetherAprs.Services.Ports;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AetherAprs.Data.Ports;

/// <summary>Durable port configuration behind the serialized runtime writer.</summary>
internal sealed class PortRegistry(SqliteRuntimeWriter writer, SqliteRuntimeReader reader, ISecretProtector secretProtector) : IPortRegistry
{
    public Task<IReadOnlyList<PortConfig>> LoadAsync(CancellationToken cancellationToken = default) =>
        reader.QueryAsync<IReadOnlyList<PortConfig>>(async (context, token) =>
        {
            var records = await context.Ports.AsNoTracking().ToListAsync(token).ConfigureAwait(false);
            return MapAll(records);
        }, cancellationToken);

    public async Task<IReadOnlyList<PortConfig>> SaveAsync(PortConfig port, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(port);
        var protectedPasscode = ProtectPasscode(port);
        var records = await writer.EnqueueAsync("port.save", async (context, token) =>
        {
            var existing = await context.Ports.FindAsync([port.Id], token).ConfigureAwait(false);
            if (existing is null)
            {
                context.Ports.Add(PortRecordMapper.ToRecord(port, protectedPasscode));
            }
            else
            {
                PortRecordMapper.CopyTo(existing, port, protectedPasscode);
            }

            await context.SaveChangesAsync(token).ConfigureAwait(false);
            return await context.Ports.AsNoTracking().ToListAsync(token).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);

        return MapAll(records);
    }

    public async Task<IReadOnlyList<PortConfig>> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var records = await writer.EnqueueAsync("port.delete", async (context, token) =>
        {
            var existing = await context.Ports.FindAsync([id], token).ConfigureAwait(false);
            if (existing is not null)
            {
                context.Ports.Remove(existing);
                await context.SaveChangesAsync(token).ConfigureAwait(false);
            }

            return await context.Ports.AsNoTracking().ToListAsync(token).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);

        return MapAll(records);
    }

    private List<PortConfig> MapAll(List<PortRecord> records)
    {
        var ports = new List<PortConfig>(records.Count);
        foreach (var record in records)
        {
            ports.Add(Map(record));
        }

        return ports;
    }

    private PortConfig Map(PortRecord record)
    {
        if (record.AprsIs is not { } aprsIs)
        {
            return PortRecordMapper.ToConfig(record);
        }

        // Stored passcodes may predate protection; expose the plaintext value to the
        // domain and let the next successful save persist the protected form.
        var passcode = secretProtector.Unprotect(aprsIs.Passcode);
        aprsIs.Passcode = passcode;
        return PortRecordMapper.ToConfig(record);
    }

    private string? ProtectPasscode(PortConfig port) =>
        port.TypeSettings is AprsIsSettings aprsIs ? secretProtector.Protect(aprsIs.Passcode) : null;
}
