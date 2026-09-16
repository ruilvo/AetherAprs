// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AetherAprs.Configuration;
using AetherAprs.Data;
using Microsoft.EntityFrameworkCore;

namespace AetherAprs.Tests.Helpers;

internal sealed class TempAppDatabase : IDisposable
{
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
        try
        {
            System.IO.Directory.Delete(Directory, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed class TestAppDbContextFactory(string path) : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => new(AppDbContext.CreateOptions(path));

        public Task<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }
}
