// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

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

        await using AppDbContext context = await dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        SqliteConnectionStringBuilder connection = new(context.Database.GetConnectionString())
        {
            Mode = SqliteOpenMode.ReadOnly
        };
        context.Database.SetConnectionString(connection.ToString());
        context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;

        T? result = await query(context, cancellationToken).ConfigureAwait(false);
        if (ReturnsDeferredQuery(result) || context.ChangeTracker.HasChanges())
        {
            throw new InvalidOperationException("SQLite queries must return materialized results without modifying the context.");
        }

        // A typed adapter can explicitly request tracking; still detach any returned entities.
        context.ChangeTracker.Clear();
        return result;
    }

    private static bool ReturnsDeferredQuery<T>(T result) =>
        result is IQueryable || (result is not null && IsAsyncEnumerable(result));

    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2075",
        Justification = "Only the interface list of the returned object is inspected to reject a lazy query. " +
            "Trimming keeps the interfaces a reachable type implements, and no member of the runtime type is accessed.")]
    private static bool IsAsyncEnumerable(object value) =>
        value.GetType().GetInterfaces().Any(type =>
            type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IAsyncEnumerable<>));
}
