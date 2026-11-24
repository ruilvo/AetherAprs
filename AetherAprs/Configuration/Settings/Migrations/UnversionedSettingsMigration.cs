// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace AetherAprs.Configuration.Settings.Migrations;

/// <summary>
/// Marks files that predate schema versioning as schema version 1.
/// </summary>
/// <remarks>
/// The first versioned layout needed no structural rewrite: legacy symbol
/// spellings are normalized on every load by
/// <see cref="LegacySymbolKeyNormalizer"/>, independently of this migration.
/// The migration exists so the version stamp only advances after a migration
/// step has actually run.
/// </remarks>
public sealed class UnversionedSettingsMigration : ISettingsMigration
{
    /// <inheritdoc />
    public int FromVersion => SettingsSchema.LegacyUnversionedVersion;

    /// <inheritdoc />
    public void Apply(IDictionary<string, string?> values, ILogger logger)
    {
        // Intentionally empty: version 1 introduced versioning itself, not a
        // change to the persisted property shape.
    }
}
