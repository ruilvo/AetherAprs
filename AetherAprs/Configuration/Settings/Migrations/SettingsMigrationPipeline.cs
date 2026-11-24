// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace AetherAprs.Configuration.Settings.Migrations;

/// <summary>
/// Upgrades a settings document from one schema version to the next.
/// </summary>
/// <remarks>
/// Migrations operate on the raw key/value representation of the settings
/// file (the same flattened form <see cref="Microsoft.Extensions.Configuration.IConfiguration"/>
/// exposes, using <c>:</c> as the section separator). They must be idempotent:
/// running a migration over a document it has already processed must not
/// change it again.
/// </remarks>
public interface ISettingsMigration
{
    /// <summary>
    /// Gets the schema version this migration reads.
    /// </summary>
    int FromVersion { get; }

    /// <summary>
    /// Applies the migration to <paramref name="values"/> in place.
    /// </summary>
    /// <param name="values">Flattened settings key/value pairs.</param>
    /// <param name="logger">Logger for reporting skipped or invalid values.</param>
    void Apply(IDictionary<string, string?> values, ILogger logger);
}

/// <summary>
/// Runs <see cref="ISettingsMigration"/> instances in order until the document
/// reaches the requested schema version.
/// </summary>
public static class SettingsMigrationPipeline
{
    /// <summary>
    /// Migrations known to this build, ordered by <see cref="ISettingsMigration.FromVersion"/>.
    /// </summary>
    /// <remarks>
    /// The only entry marks pre-versioning documents as version 1; that layout
    /// needed no structural rewrite. Add an entry here when a persisted
    /// property is renamed, retyped, or moved, and bump
    /// <see cref="SettingsSchema.CurrentVersion"/>.
    /// </remarks>
    private static readonly ISettingsMigration[] Migrations =
    [
        new UnversionedSettingsMigration(),
    ];

    /// <summary>
    /// Migrates <paramref name="values"/> using the migrations known to this build.
    /// </summary>
    /// <param name="values">Flattened settings key/value pairs, updated in place.</param>
    /// <param name="fromVersion">Schema version of the input document.</param>
    /// <param name="targetVersion">Schema version to reach.</param>
    /// <param name="logger">Logger for migration diagnostics.</param>
    /// <returns>The schema version of the returned document.</returns>
    public static int Migrate(
        IDictionary<string, string?> values,
        int fromVersion,
        int targetVersion,
        ILogger logger)
    {
        return Migrate(values, fromVersion, targetVersion, Migrations, logger);
    }

    /// <summary>
    /// Migrates <paramref name="values"/> using an explicit migration chain.
    /// </summary>
    /// <param name="values">Flattened settings key/value pairs, updated in place.</param>
    /// <param name="fromVersion">Schema version of the input document.</param>
    /// <param name="targetVersion">Schema version to reach.</param>
    /// <param name="migrations">The migration chain to apply.</param>
    /// <param name="logger">Logger for migration diagnostics.</param>
    /// <returns>The schema version of the returned document.</returns>
    public static int Migrate(
        IDictionary<string, string?> values,
        int fromVersion,
        int targetVersion,
        IReadOnlyList<ISettingsMigration> migrations,
        ILogger logger)
    {
        var version = fromVersion;
        while (version < targetVersion)
        {
            ISettingsMigration? migration = null;
            foreach (var candidate in migrations)
            {
                if (candidate.FromVersion == version)
                {
                    migration = candidate;
                    break;
                }
            }

            if (migration is null)
            {
                logger.LogWarning(
                    "No settings migration from schema version {FromVersion}; stopping at {Version}.",
                    version,
                    version);
                break;
            }

            logger.LogInformation(
                "Migrating settings from schema version {FromVersion} to {ToVersion}.",
                migration.FromVersion,
                migration.FromVersion + 1);

            migration.Apply(values, logger);
            version++;
        }

        return version;
    }
}
