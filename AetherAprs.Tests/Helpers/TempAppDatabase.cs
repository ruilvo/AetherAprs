// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AetherAprs.Configuration;
using AetherAprs.Data;
using AetherAprs.Data.Mappers;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AetherAprs.Tests.Helpers;

internal sealed class TempAppDatabase : IDisposable
{
    private const int DeleteAttempts = 80;
    private static readonly TimeSpan DeleteRetryDelay = TimeSpan.FromMilliseconds(25);
    private static readonly ILogger<TempAppDatabase> Logger = NullLogger<TempAppDatabase>.Instance;

    private TempAppDatabase(string directory, IDbContextFactory<AppDbContext> factory)
    {
        Directory = directory;
        Factory = factory;
    }

    public string Directory { get; }

    public IDbContextFactory<AppDbContext> Factory { get; }

    public static TempAppDatabase Create(params PortConfig[] ports)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"AetherAprsTests-{Guid.NewGuid():N}");
        System.IO.Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, AppDbContext.DatabaseFileName);
        var factory = new TestAppDbContextFactory(path);
        using (var db = factory.CreateDbContext())
        {
            db.Database.EnsureCreated();
            foreach (var port in ports)
            {
                db.Ports.Add(PortRecordMapper.ToRecord(port));
            }

            db.SaveChanges();
        }

        return new TempAppDatabase(directory, factory);
    }

    public static TempAppDatabase CreateEmpty()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"AetherAprsTests-{Guid.NewGuid():N}");
        System.IO.Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, AppDbContext.DatabaseFileName);
        return new TempAppDatabase(directory, new TestAppDbContextFactory(path));
    }

    public AppDbContext CreateContext() => Factory.CreateDbContext();

    public void Dispose()
    {
        // Idle pooled connections and in-flight background writes can keep the
        // SQLite file open, which makes the recursive delete fail on Windows.
        // Close idle pools, then retry briefly until the handle is released.
        SqliteConnection.ClearAllPools();

        for (var attempt = 1; attempt <= DeleteAttempts; attempt++)
        {
            try
            {
                System.IO.Directory.Delete(Directory, recursive: true);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (attempt == DeleteAttempts)
                {
                    Logger.LogWarning(ex, "Failed to delete temp directory {Directory}", Directory);
                    return;
                }

                Task.Delay(DeleteRetryDelay).GetAwaiter().GetResult();
            }
        }
    }

    private sealed class TestAppDbContextFactory(string path) : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => new(AppDbContext.CreateOptions(path));

        public Task<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }
}
