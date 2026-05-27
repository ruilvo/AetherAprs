// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

namespace AetherAprs.Services.Configuration;

/// <summary>
/// Provides access to and persistence of application settings.
/// </summary>
public interface IConfigurationService
{
    /// <summary>
    /// Gets the currently loaded application settings.
    /// </summary>
    AppSettings Settings { get; }

    /// <summary>
    /// Persists the supplied settings to the underlying configuration store.
    /// </summary>
    /// <param name="newSettings">The settings to persist.</param>
    /// <returns><c>true</c> if the settings were persisted successfully; otherwise <c>false</c>.</returns>
    bool SaveSettings(AppSettings newSettings);

    /// <summary>
    /// Returns the default settings as defined in the bundled <c>appsettings.json</c>, ignoring any user overrides.
    /// </summary>
    AppSettings GetDefaults();
}
