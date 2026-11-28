// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.IO;
using System.Linq;
using System.Threading;

using AetherAprs.Data.Entities;
using AetherAprs.Data.Mappers;
using AetherAprs.Models;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AetherAprs.Data;

/// <summary>
/// Thrown when a schema upgrade could not be applied and the database has been
/// preserved so the previous state is never silently lost.
/// </summary>
public sealed class DatabaseUpgradeException : Exception
{
    /// <summary>
    /// Initializes a new upgrade failure.
    /// </summary>
    /// <param name="message">A description of what failed.</param>
    /// <param name="backupPath">
    /// The preserved copy of the database before the upgrade, or
    /// <see langword="null"/> when no copy could be taken.
    /// </param>
    /// <param name="innerException">The migration failure.</param>
    public DatabaseUpgradeException(string message, string? backupPath, Exception innerException)
        : base(message, innerException) => BackupPath = backupPath;

    /// <summary>
    /// Gets the path of the preserved database copy, when one was taken.
    /// </summary>
    public string? BackupPath { get; }
}

/// <summary>
/// Applies EF migrations and seeds default data on first run.
/// </summary>
public sealed class AppSavedDataInitializer(
    IDbContextFactory<AppDbContext> dbContextFactory,
    ILogger<AppSavedDataInitializer> logger)
{
    private readonly Lock _gate = new();
    private bool _initialized;

    public void Initialize()
    {
        lock (_gate)
        {
            if (_initialized)
            {
                return;
            }

            logger.LogInformation("Initializing database and seeding default data");
            using AppDbContext db = dbContextFactory.CreateDbContext();

            // Enable WAL journaling so concurrent contexts (packet storage while
            // queries run) do not collide on the default rollback journal (M7).
            AppDbContext.ApplyProductionPragmas(db);

            MigratePreservingOnFailure(db);
            SeedBeaconingDefaults(db);
            _initialized = true;
            logger.LogInformation("Database initialization complete");
        }
    }

    /// <summary>
    /// Applies pending migrations, keeping a copy of the database from before the
    /// upgrade so a failed upgrade never strands the operator without their data.
    /// </summary>
    /// <remarks>
    /// A fresh database has nothing to preserve and is migrated in place. For an
    /// existing database the copy is taken before the first migration runs, so it
    /// holds the last schema and data that worked. When the migration fails the
    /// failure is reported as <see cref="DatabaseUpgradeException"/> with the
    /// backup path, instead of a bare provider exception the caller cannot act on.
    /// </remarks>
    private void MigratePreservingOnFailure(AppDbContext db)
    {
        string[] pending = [.. db.Database.GetPendingMigrations()];
        if (pending.Length == 0)
        {
            return;
        }

        string? databasePath = GetDatabasePath(db);
        string? backupPath = databasePath is null
            ? null
            : PreserveBeforeUpgrade(databasePath, pending.Length);

        try
        {
            logger.LogInformation(
                "Applying {PendingCount} pending database migration(s).",
                pending.Length);
            db.Database.Migrate();
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Database upgrade failed. {Preserved}",
                backupPath is null
                    ? "No pre-upgrade copy could be taken."
                    : $"The database from before the upgrade was preserved at {backupPath}.");

            throw new DatabaseUpgradeException(
                backupPath is null
                    ? "The database upgrade failed and no pre-upgrade copy could be taken."
                    : $"The database upgrade failed. The previous database is preserved at {backupPath}.",
                backupPath,
                exception);
        }
    }

    /// <summary>
    /// Copies the database file aside before an upgrade. At most one copy is kept
    /// per file, so repeated failed starts cannot fill the volume with backups.
    /// </summary>
    private string? PreserveBeforeUpgrade(string databasePath, int pendingCount)
    {
        try
        {
            if (!File.Exists(databasePath) || new FileInfo(databasePath).Length == 0)
            {
                // A brand-new, empty file has no user data worth preserving.
                return null;
            }

            string backupPath = $"{databasePath}.pre-migration.bak";
            if (File.Exists(backupPath))
            {
                logger.LogDebug(
                    "Keeping the existing pre-upgrade database copy at {BackupPath}.",
                    backupPath);
                return backupPath;
            }

            File.Copy(databasePath, backupPath, overwrite: false);
            logger.LogInformation(
                "Preserved the database at {BackupPath} before applying {PendingCount} migration(s).",
                backupPath,
                pendingCount);
            return backupPath;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Preserving is a safety net, not a precondition: a failure to copy
            // must not stop a migration that would otherwise succeed.
            logger.LogWarning(
                exception,
                "Could not preserve the database before upgrading.");
            return null;
        }
    }

    /// <summary>
    /// Reads the physical database path from the open connection.
    /// </summary>
    private static string? GetDatabasePath(AppDbContext db)
    {
        string? dataSource = db.Database.GetDbConnection().DataSource;
        return string.IsNullOrWhiteSpace(dataSource) ? null : dataSource;
    }

    private void SeedBeaconingDefaults(AppDbContext db)
    {
        if (!db.BeaconConfigs.Any())
        {
            logger.LogInformation("Seeding default beacon configurations");
            db.BeaconConfigs.AddRange(
                BeaconConfigMapper.ToRecord(BeaconConfig.CreateWalkPreset()),
                BeaconConfigMapper.ToRecord(BeaconConfig.CreateDrivePreset()),
                BeaconConfigMapper.ToRecord(BeaconConfig.CreateCustomPreset()));
        }

        if (!db.BeaconingState.Any())
        {
            logger.LogInformation("Creating default beaconing state");
            db.BeaconingState.Add(new BeaconingStateRecord());
        }

        db.SaveChanges();
    }
}
