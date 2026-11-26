// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

namespace AetherAprs.Configuration.Settings.Documents;

/// <summary>
/// Mutable binding target for the settings file.
/// </summary>
/// <remarks>
/// The configuration binder writes one key at a time and relies on a throwing
/// setter to reject a single invalid value without discarding the values around
/// it. That requires a mutable graph, so this document exists purely as the
/// load-time shape of the file. It never leaves the configuration service: after
/// binding, <see cref="ToSettings"/> publishes an immutable
/// <see cref="AppSettings"/> snapshot instead.
/// </remarks>
internal sealed class AppSettingsDocument
{
    public int SchemaVersion { get; set; } = SettingsSchema.CurrentVersion;

    public AprsSettingsDocument Aprs { get; set; } = new();

    public MessageTemplatesSettingsDocument MessageTemplates { get; set; } = new();

    public MapSettingsDocument Map { get; set; } = new();

    public ThemeMode Theme { get; set; } = ThemeMode.System;

    public AppSettings ToSettings() => new()
    {
        SchemaVersion = SchemaVersion,
        Aprs = Aprs.ToSettings(),
        MessageTemplates = MessageTemplates.ToSettings(),
        Map = Map.ToSettings(),
        Theme = Theme,
    };
}
