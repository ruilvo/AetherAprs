// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using Microsoft.EntityFrameworkCore.Design;
using System.IO;

namespace AetherAprs.Data;

/// <summary>
/// Design-time factory used by <c>dotnet ef</c> to generate migrations.
/// </summary>
public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        // Use a temp path so `dotnet ef` does not drop a stray aetheraprs.db in
        // the working directory (L2).
        var tempPath = Path.Combine(Path.GetTempPath(), AppDbContext.DatabaseFileName);
        return new AppDbContext(AppDbContext.CreateOptions(tempPath));
    }
}
