// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using Microsoft.Extensions.Logging;

namespace AetherAprs.Configuration.Settings;

public class AppSettings
{
    public LoggerFilterOptions Logging { get; set; } = new();

    public AprsSettings Aprs { get; set; } = new();

    public MessageTemplatesSettings MessageTemplates { get; set; } = new();

    public MapSettings Map { get; set; } = new();

    /// <summary>
    /// The application color theme. Defaults to following the operating system.
    /// </summary>
    public ThemeMode Theme { get; set; } = ThemeMode.System;
}
