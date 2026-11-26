// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Configuration;
using AetherAprs.Data.Entities;
using AetherAprs.Data.Mappers;
using AetherAprs.Services.Platform;
using AetherAprs.Services.Ports;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AetherAprs.Data.Ports;

/// <summary>Durable port configuration behind the serialized runtime writer.</summary>
internal sealed class PortRegistry(
    SqliteRuntimeWriter writer,
    SqliteRuntimeReader reader,
    ISecretProtector secretProtector,
    ILogger<PortRegistry> logger) : IPortRegistry
{
    public Task<IReadOnlyList<PortConfig>> LoadAsync(CancellationToken cancellationToken = default) =>
        reader.QueryAsync<IReadOnlyList<PortConfig>>(async (context, token) =>
        {
            List<PortRecord> records = await context.Ports.AsNoTracking().ToListAsync(token).ConfigureAwait(false);
            return MapAll(records);
        }, cancellationToken);

    public async Task<IReadOnlyList<PortConfig>> SaveAsync(PortConfig port, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(port);
        string? protectedPasscode = ProtectPasscode(port);
        List<PortRecord> records = await writer.EnqueueAsync("port.save", async (context, token) =>
        {
            PortRecord? existing = await context.Ports.FindAsync([port.Id], token).ConfigureAwait(false);
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
        List<PortRecord> records = await writer.EnqueueAsync("port.delete", async (context, token) =>
        {
            PortRecord? existing = await context.Ports.FindAsync([id], token).ConfigureAwait(false);
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
        List<PortConfig> ports = [with(records.Count)];
        foreach (PortRecord record in records)
        {
            ports.Add(Map(record));
        }

        return ports;
    }

    private PortConfig Map(PortRecord record)
    {
        PortConfig port = PortRecordMapper.ToConfig(record);
        if (port.TypeSettings is not AprsIsSettings aprsIs)
        {
            return port;
        }

        try
        {
            // Stored passcodes may predate protection; expose the plaintext value to the
            // domain and let the next successful save persist the protected form.
            aprsIs.Passcode = secretProtector.Unprotect(aprsIs.Passcode);
        }
        catch (SecretUnavailableException ex)
        {
            // One lost secret must not hide the other ports: keep this port's settings,
            // drop the credential, and let the operator enter it again.
            logger.LogWarning(
                ex,
                "Stored credentials for port {PortId} are unavailable; loading it receive-only until the passcode is entered again",
                record.Id);
            aprsIs.Passcode = AprsIsSettings.DefaultPasscode;
            port.CredentialsUnavailable = true;
        }

        return port;
    }

    private string? ProtectPasscode(PortConfig port) =>
        port.TypeSettings is AprsIsSettings aprsIs ? secretProtector.Protect(aprsIs.Passcode) : null;
}
