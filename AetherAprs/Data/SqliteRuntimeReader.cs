// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AetherAprs.Data;

/// <summary>Owns short-lived read-only contexts for typed persistence adapters in the Data layer.</summary>
internal sealed class SqliteRuntimeReader(IDbContextFactory<AppDbContext> dbContextFactory)
{
    /// <summary>Executes a fully materialized read in its own untracked context.</summary>
    internal async Task<T> QueryAsync<T>(
        Func<AppDbContext, CancellationToken, Task<T>> query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        await using var context = await dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var connection = new SqliteConnectionStringBuilder(context.Database.GetConnectionString())
        {
            Mode = SqliteOpenMode.ReadOnly
        };
        context.Database.SetConnectionString(connection.ToString());
        context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;

        var result = await query(context, cancellationToken).ConfigureAwait(false);
        if (result is IQueryable || result?.GetType().GetInterfaces().Any(type =>
                type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IAsyncEnumerable<>)) == true ||
            context.ChangeTracker.HasChanges())
        {
            throw new InvalidOperationException("SQLite queries must return materialized results without modifying the context.");
        }

        // A typed adapter can explicitly request tracking; still detach any returned entities.
        context.ChangeTracker.Clear();
        return result;
    }
}
