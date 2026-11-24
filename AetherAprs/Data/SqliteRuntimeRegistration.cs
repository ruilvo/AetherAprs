// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System;

namespace AetherAprs.Data;

internal static class SqliteRuntimeRegistration
{
    internal static void AddSqliteRuntime(this IServiceCollection services)
    {
        services.AddSingleton(provider => new SqliteRuntimeWriter(GetInitializedFactory(provider)));
        services.AddSingleton(provider => new SqliteRuntimeReader(GetInitializedFactory(provider)));
    }

    private static IDbContextFactory<AppDbContext> GetInitializedFactory(IServiceProvider provider)
    {
        // Share the exclusive, idempotent bootstrap with normal application startup.
        provider.GetRequiredService<AppSavedDataInitializer>().Initialize();
        return provider.GetRequiredService<IDbContextFactory<AppDbContext>>();
    }
}
