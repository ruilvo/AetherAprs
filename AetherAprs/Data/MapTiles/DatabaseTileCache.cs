// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Data.Entities;
using BruTile;
using BruTile.Cache;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AetherAprs.Data.MapTiles;

/// <summary>
/// Persistent map tile cache backed by the application database.
/// </summary>
/// <remarks>
/// BruTile's cache contract is synchronous, so the members below block on the
/// runtime writer and reader. That is acceptable because BruTile calls the cache
/// from its own tile-fetch thread, never from the UI thread, and each call is one
/// indexed row operation. A cache miss is normal: losing the whole table only
/// costs a re-download, so every failure is reported as a miss instead of
/// surfacing as a map error.
/// </remarks>
internal sealed class DatabaseTileCache(
    SqliteRuntimeWriter writer,
    SqliteRuntimeReader reader,
    ILogger<DatabaseTileCache> logger) : IPersistentCache<byte[]>
{
    /// <summary>
    /// How long a cached tile is kept. Tiles are disposable, so the oldest rows
    /// are pruned once the table grows past this age.
    /// </summary>
    internal static readonly TimeSpan Retention = TimeSpan.FromDays(30);

    /// <summary>
    /// Most rows pruned per write, so a prune never turns one map tile request
    /// into an unbounded delete.
    /// </summary>
    internal const int PruneBatchSize = 500;

    private readonly Lock _pruneGate = new();
    private long _nextPruneTicks;

    /// <inheritdoc />
    public void Add(TileIndex index, byte[] tile)
    {
        ArgumentNullException.ThrowIfNull(tile);

        try
        {
            writer.EnqueueAsync("maptile.add", async (context, token) =>
            {
                MapTileRecord? record = await context.MapTiles
                    .FindAsync([index.Level, index.Col, index.Row], token).ConfigureAwait(false);
                if (record is null)
                {
                    context.MapTiles.Add(new MapTileRecord
                    {
                        ZoomLevel = index.Level,
                        TileColumn = index.Col,
                        TileRow = index.Row,
                        Data = tile,
                        UpdatedAt = DateTime.UtcNow
                    });
                }
                else
                {
                    record.Data = tile;
                    record.UpdatedAt = DateTime.UtcNow;
                }

                await context.SaveChangesAsync(token).ConfigureAwait(false);
                return true;
            }, CancellationToken.None).GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            // A cache write is an optimisation. Failing it must not break rendering.
            logger.LogDebug(exception, "Could not cache map tile {Level}/{Column}/{Row}.", index.Level, index.Col, index.Row);
            return;
        }

        PruneIfDue();
    }

    /// <inheritdoc />
    public byte[]? Find(TileIndex index)
    {
        try
        {
            return reader.QueryAsync(async (context, token) =>
            {
                MapTileRecord? record = await context.MapTiles.AsNoTracking()
                    .FirstOrDefaultAsync(
                        candidate => candidate.ZoomLevel == index.Level
                            && candidate.TileColumn == index.Col
                            && candidate.TileRow == index.Row,
                        token).ConfigureAwait(false);
                return record?.Data;
            }, CancellationToken.None).GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            logger.LogDebug(exception, "Could not read cached map tile {Level}/{Column}/{Row}.", index.Level, index.Col, index.Row);
            return null;
        }
    }

    /// <inheritdoc />
    public void Remove(TileIndex index)
    {
        try
        {
            writer.EnqueueAsync("maptile.remove", (context, token) =>
                context.MapTiles
                    .Where(tile => tile.ZoomLevel == index.Level
                        && tile.TileColumn == index.Col
                        && tile.TileRow == index.Row)
                    .ExecuteDeleteAsync(token), CancellationToken.None)
                .GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            logger.LogDebug(exception, "Could not evict cached map tile {Level}/{Column}/{Row}.", index.Level, index.Col, index.Row);
        }
    }

    /// <summary>
    /// Drops tiles older than <see cref="Retention"/> in one bounded batch. Runs at
    /// most once per hour so a pan across the map does not schedule a delete per
    /// tile.
    /// </summary>
    private void PruneIfDue()
    {
        long now = DateTimeOffset.UtcNow.UtcTicks;
        lock (_pruneGate)
        {
            if (now < Interlocked.Read(ref _nextPruneTicks))
            {
                return;
            }

            Interlocked.Exchange(ref _nextPruneTicks, now + TimeSpan.FromHours(1).Ticks);
        }

        DateTime cutoff = DateTime.UtcNow.Subtract(Retention);
        try
        {
            writer.EnqueueAsync("maptile.prune", async (context, token) =>
            {
                List<MapTileRecord> stale = await context.MapTiles
                    .Where(tile => tile.UpdatedAt < cutoff)
                    .OrderBy(tile => tile.UpdatedAt)
                    .Take(PruneBatchSize)
                    .ToListAsync(token).ConfigureAwait(false);
                if (stale.Count > 0)
                {
                    context.MapTiles.RemoveRange(stale);
                    await context.SaveChangesAsync(token).ConfigureAwait(false);
                }

                return stale.Count;
            }, CancellationToken.None).GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            logger.LogDebug(exception, "Could not prune stale cached map tiles.");
        }
    }
}
