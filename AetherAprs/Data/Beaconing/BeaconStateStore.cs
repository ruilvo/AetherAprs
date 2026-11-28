// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using AetherAprs.Data.Entities;
using AetherAprs.Data.Mappers;
using AetherAprs.Models;
using AetherAprs.Services.Beaconing;

using Microsoft.EntityFrameworkCore;

namespace AetherAprs.Data.Beaconing;

/// <summary>Durable beacon configuration and mode behind the serialized runtime writer.</summary>
internal sealed class BeaconStateStore(SqliteRuntimeWriter writer, SqliteRuntimeReader reader) : IBeaconStateStore
{
    public Task<BeaconStateSnapshot> LoadAsync(CancellationToken cancellationToken = default) =>
        reader.QueryAsync<BeaconStateSnapshot>(async (context, token) =>
        {
            System.Collections.Generic.List<BeaconConfigRecord> records = await context.BeaconConfigs.AsNoTracking().ToListAsync(token).ConfigureAwait(false);
            BeaconingStateRecord? state = await context.BeaconingState.AsNoTracking()
                .FirstOrDefaultAsync(token).ConfigureAwait(false);

            return new BeaconStateSnapshot(
                Find(records, DynamicBeaconMode.Walk),
                Find(records, DynamicBeaconMode.Drive),
                Find(records, DynamicBeaconMode.Custom),
                state?.ActiveMode ?? DynamicBeaconMode.Walk);
        }, cancellationToken);

    public Task<BeaconStateSnapshot> SaveConfigurationAsync(
        BeaconConfig configuration, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return writer.EnqueueAsync("beacon.save-config", async (context, token) =>
        {
            BeaconConfigRecord? existing = await context.BeaconConfigs
                .FindAsync([configuration.Mode], token).ConfigureAwait(false);
            if (existing is null)
            {
                context.BeaconConfigs.Add(BeaconConfigMapper.ToRecord(configuration));
            }
            else
            {
                BeaconConfigMapper.CopyTo(existing, configuration);
            }

            await context.SaveChangesAsync(token).ConfigureAwait(false);
            return await ReadSnapshotAsync(context, token).ConfigureAwait(false);
        }, cancellationToken);
    }

    public Task<BeaconStateSnapshot> SaveActiveModeAsync(
        DynamicBeaconMode mode, CancellationToken cancellationToken = default) =>
        writer.EnqueueAsync("beacon.save-mode", async (context, token) =>
        {
            BeaconingStateRecord? state = await context.BeaconingState
                .FindAsync([BeaconingStateRecord.SingletonId], token).ConfigureAwait(false);
            if (state is null)
            {
                context.BeaconingState.Add(new BeaconingStateRecord { ActiveMode = mode });
            }
            else
            {
                state.ActiveMode = mode;
            }

            await context.SaveChangesAsync(token).ConfigureAwait(false);
            return await ReadSnapshotAsync(context, token).ConfigureAwait(false);
        }, cancellationToken);

    private static async Task<BeaconStateSnapshot> ReadSnapshotAsync(
        AppDbContext context, CancellationToken cancellationToken)
    {
        System.Collections.Generic.List<BeaconConfigRecord> records = await context.BeaconConfigs.AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);
        BeaconingStateRecord? state = await context.BeaconingState.AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

        return new BeaconStateSnapshot(
            Find(records, DynamicBeaconMode.Walk),
            Find(records, DynamicBeaconMode.Drive),
            Find(records, DynamicBeaconMode.Custom),
            state?.ActiveMode ?? DynamicBeaconMode.Walk);
    }

    /// <summary>Falls back to the compiled preset when a mode has never been stored.</summary>
    private static BeaconConfig Find(System.Collections.Generic.List<BeaconConfigRecord> records, DynamicBeaconMode mode)
    {
        BeaconConfigRecord? record = records.FirstOrDefault(candidate => candidate.Mode == mode);
        if (record is not null)
        {
            return BeaconConfigMapper.ToConfig(record);
        }

        return mode switch
        {
            DynamicBeaconMode.Walk => BeaconConfig.CreateWalkPreset(),
            DynamicBeaconMode.Drive => BeaconConfig.CreateDrivePreset(),
            _ => BeaconConfig.CreateCustomPreset()
        };
    }
}
