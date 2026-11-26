// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using Microsoft.Extensions.Logging;
using System.Text.Json.Serialization;

namespace AetherAprs.Configuration.Settings;

/// <summary>
/// The complete application configuration. Instances are immutable snapshots:
/// callers read the published instance from
/// <see cref="Services.Configuration.IConfigurationService.Current"/> and produce
/// a replacement with <see cref="Services.Configuration.IConfigurationService.UpdateAsync"/>.
/// </summary>
public sealed record AppSettings
{
    /// <summary>
    /// On-disk schema version of this settings document.
    /// </summary>
    /// <remarks>
    /// Written on every save so a later build can tell which migrations to run.
    /// Files that predate versioning load as
    /// <see cref="SettingsSchema.LegacyUnversionedVersion"/>.
    /// </remarks>
    [JsonPropertyName(SettingsSchema.VersionPropertyName)]
    public int SchemaVersion { get; init; } = SettingsSchema.CurrentVersion;

    /// <summary>
    /// Provider logging model. The logging builder owns its behavior; this
    /// snapshot only carries it so a save does not drop the section.
    /// </summary>
    public LoggerFilterOptions Logging { get; init; } = new();

    /// <summary>
    /// Gets the station identity and APRS behavior.
    /// </summary>
    public AprsSettings Aprs { get; init; } = new();

    /// <summary>
    /// Gets the user-defined quick message templates.
    /// </summary>
    public MessageTemplatesSettings MessageTemplates { get; init; } = new();

    /// <summary>
    /// Gets the map display settings.
    /// </summary>
    public MapSettings Map { get; init; } = new();

    /// <summary>
    /// Gets the application color theme. Defaults to following the operating system.
    /// </summary>
    public ThemeMode Theme { get; init; } = ThemeMode.System;
}
