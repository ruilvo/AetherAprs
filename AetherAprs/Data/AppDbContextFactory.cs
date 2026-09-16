// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using Microsoft.EntityFrameworkCore.Design;

namespace AetherAprs.Data;

/// <summary>
/// Design-time factory used by <c>dotnet ef</c> to generate migrations.
/// </summary>
public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        return new AppDbContext(AppDbContext.CreateOptions(AppDbContext.DatabaseFileName));
    }
}
