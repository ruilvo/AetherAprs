// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Configuration;
using AetherAprs.Configuration.Settings;
using System.Threading.Tasks;

namespace AetherAprs.Services.Configuration;

public interface IConfigurationService
{
    /// <summary>
    /// Gets the strongly-typed application settings.
    /// </summary>
    AppSettings Settings { get; }

    /// <summary>
    /// Saves the current settings to the configuration file.
    /// </summary>
    Task SaveSettingsAsync();

    /// <summary>
    /// Returns a deep-cloned snapshot of the current settings for thread-safe reading.
    /// Use this when you need consistent multi-property reads (e.g., callsign + SSID together).
    /// For write operations, use UpdateSettingsAsync to ensure thread-safe read-modify-write cycles.
    /// </summary>
    /// <returns>A deep copy of the current settings.</returns>
    AppSettings GetSettingsSnapshot();
}
