// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

namespace AetherAprs.Configuration.Settings;

/// <summary>
/// Describes the on-disk schema of <c>appsettings.json</c>.
/// </summary>
/// <remarks>
/// Every persisted settings file carries a <c>SchemaVersion</c> property.
/// Files written before versioning existed have no such property and are
/// treated as version <see cref="LegacyUnversionedVersion"/>. On load,
/// <see cref="Migrations.SettingsMigrationPipeline"/> upgrades older files to
/// <see cref="CurrentVersion"/>; newer files are left untouched and backed up
/// so a downgrade cannot destroy settings written by a newer build.
/// </remarks>
public static class SettingsSchema
{
    /// <summary>
    /// Name of the property that stores the schema version in the settings file.
    /// </summary>
    public const string VersionPropertyName = "SchemaVersion";

    /// <summary>
    /// Version assumed for files that predate schema versioning.
    /// </summary>
    public const int LegacyUnversionedVersion = 0;

    /// <summary>
    /// Schema version written by this build.
    /// </summary>
    /// <remarks>
    /// Version 1 is the first versioned layout. Bump this and add an
    /// <see cref="Migrations.ISettingsMigration"/> whenever a persisted
    /// property is renamed, retyped, or moved, so existing files upgrade
    /// instead of silently losing the old value.
    /// </remarks>
    public const int CurrentVersion = 1;
}
