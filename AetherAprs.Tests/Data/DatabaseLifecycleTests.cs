// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.IO;
using System.Linq;

using AetherAprs.Data;
using AetherAprs.Data.Entities;
using AetherAprs.Models;
using AetherAprs.Tests.Helpers;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

using Xunit;

namespace AetherAprs.Tests.Data;

/// <summary>
/// Covers the on-disk lifecycle of the application database: a fresh file is
/// migrated and seeded, the migrated file reopens with its data, and a failed
/// upgrade preserves what was there instead of leaving a half-applied schema.
/// </summary>
public sealed class DatabaseLifecycleTests
{
    [Fact]
    public void MigratedDatabaseReopensWithItsSyntheticData()
    {
        using TempAppDatabase db = TempAppDatabase.CreateEmpty();

        CreateInitializer(db).Initialize();

        using (AppDbContext writer = db.CreateContext())
        {
            // Two packets with distinct receipt times stand in for a real history.
            writer.Packets.Add(CreatePacket("CT7ALW", new DateTime(2026, 11, 20, 8, 0, 0, DateTimeKind.Utc)));
            writer.Packets.Add(CreatePacket("N1ABC", new DateTime(2026, 11, 21, 9, 30, 0, DateTimeKind.Utc)));
            writer.SaveChanges();
        }

        // Release every pooled handle so the reopen exercises the file, not a cache.
        SqliteConnection.ClearAllPools();

        using AppDbContext reopened = db.CreateContext();
        Assert.Equal(2, reopened.Packets.Count());
        Assert.Equal(
            ["CT7ALW", "N1ABC"],
            reopened.Packets.OrderBy(packet => packet.Timestamp).Select(packet => packet.SourceBase));

        // The seeded defaults survive the same reopen.
        Assert.Equal(3, reopened.BeaconConfigs.Count());
        Assert.Equal(DynamicBeaconMode.Walk, Assert.Single(reopened.BeaconingState).ActiveMode);
    }

    [Fact]
    public void ReopenAfterMigrationIsIdempotent()
    {
        using TempAppDatabase db = TempAppDatabase.CreateEmpty();

        CreateInitializer(db).Initialize();

        // A second initializer over the migrated file must not re-run migrations
        // or re-seed, which is what an app restart does.
        CreateInitializer(db).Initialize();

        using AppDbContext context = db.CreateContext();
        Assert.Equal(3, context.BeaconConfigs.Count());
        Assert.Single(context.BeaconingState);
    }

    [Fact]
    public void FailedUpgradePreservesTheDatabaseAndReportsTheCopy()
    {
        // EnsureCreated produces a populated schema with no migration history, so
        // the next migration attempt collides with the existing tables. That is
        // exactly the shape of a broken upgrade.
        using TempAppDatabase db = TempAppDatabase.Create();

        using (AppDbContext seed = db.CreateContext())
        {
            seed.Packets.Add(CreatePacket("CT7ALW", new DateTime(2026, 11, 20, 8, 0, 0, DateTimeKind.Utc)));
            seed.SaveChanges();
        }

        DatabaseUpgradeException exception = Assert.Throws<DatabaseUpgradeException>(
            () => CreateInitializer(db).Initialize());

        Assert.NotNull(exception.BackupPath);
        Assert.True(File.Exists(exception.BackupPath));

        // The preserved copy still holds the row, which is the whole point: the
        // operator can recover the data that existed before the failed upgrade.
        string preservedPath = exception.BackupPath!;
        Assert.Equal(1, CountPacketsInFile(preservedPath));
    }

    [Fact]
    public void FailedUpgradeDoesNotLatchSoTheNextAttemptRetries()
    {
        using TempAppDatabase db = TempAppDatabase.Create();
        AppSavedDataInitializer initializer = CreateInitializer(db);

        Assert.Throws<DatabaseUpgradeException>(() => initializer.Initialize());

        // A failed upgrade must stay retryable: a later launch that succeeds (for
        // example after the operator restores the backup) has to be able to try.
        Assert.Throws<DatabaseUpgradeException>(() => initializer.Initialize());
    }

    [Fact]
    public void PreservedCopyIsReusedInsteadOfOverwrittenOnEachRetry()
    {
        using TempAppDatabase db = TempAppDatabase.Create();
        AppSavedDataInitializer initializer = CreateInitializer(db);

        Assert.Throws<DatabaseUpgradeException>(() => initializer.Initialize());
        DatabaseUpgradeException second = Assert.Throws<DatabaseUpgradeException>(
            () => initializer.Initialize());

        // At most one pre-upgrade copy is kept, so repeated failed starts cannot
        // fill the volume with backups.
        Assert.NotNull(second.BackupPath);
        string[] backups = Directory.GetFiles(db.Directory, "*.pre-migration.bak");
        Assert.Single(backups);
    }

    private static AppSavedDataInitializer CreateInitializer(TempAppDatabase db) =>
        new(db.Factory, NullLogger<AppSavedDataInitializer>.Instance);

    private static PacketRecord CreatePacket(string source, DateTime timestampUtc) => new()
    {
        SourceBase = source,
        SourceSsid = 0,
        DestinationBase = "APRS",
        DestinationSsid = 0,
        PacketType = "Position",
        RawInfo = "!4903.50N/07201.75W-Test",
        RawFrameHex = "00",
        RawPath = "WIDE1-1",
        IsOutbound = false,
        Timestamp = timestampUtc,
        PacketTimestamp = timestampUtc,
    };

    private static int CountPacketsInFile(string path)
    {
        using AppDbContext context = new(AppDbContext.CreateOptions(path, pooling: false));
        return context.Packets.Count();
    }
}
