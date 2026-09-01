// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later
using Microsoft.Extensions.Logging;

namespace AetherAprs.Configuration;

/// <summary>
/// Root application settings.
/// </summary>
public class AppSettings
{
    /// <summary>
    /// Gets or sets the logging configuration.
    /// </summary>
    public LoggerFilterOptions Logging { get; set; } = new();

    /// <summary>
    /// Gets or sets the application configuration.
    /// </summary>
    public ApplicationSettings Application { get; set; } = new();
}

/// <summary>
/// Application-specific settings.
/// </summary>
public class ApplicationSettings
{
    /// <summary>
    /// Gets or sets the application name.
    /// </summary>
    public string Name { get; set; } = "AetherAprs";

    /// <summary>
    /// Gets or sets the application version.
    /// </summary>
    public string Version { get; set; } = "1.0.0";
}
