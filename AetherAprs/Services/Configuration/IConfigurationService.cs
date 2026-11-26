// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using AetherAprs.Configuration;
using AetherAprs.Configuration.Settings;
using System.Threading.Tasks;

namespace AetherAprs.Services.Configuration;

public interface IConfigurationService
{
    /// <summary>
    /// Raised after settings have been saved, so observers (map layers, packet
    /// lists) can refresh the filters they derive from settings.
    /// </summary>
    event EventHandler? SettingsChanged;

    /// <summary>
    /// Gets the current strongly-typed application settings. Treat the returned
    /// object as read-only and retrieve it again after updates.
    /// </summary>
    AppSettings Settings { get; }

    /// <summary>
    /// Saves the current settings to the configuration file.
    /// Prefer <see cref="UpdateSettingsAsync"/> for new writes so mutation and
    /// persistence occur inside one serialized update boundary.
    /// </summary>
    Task SaveSettingsAsync();

    /// <summary>
    /// Returns a deep-cloned snapshot of the current settings for thread-safe reading.
    /// Use this when you need consistent multi-property reads (e.g., callsign + SSID together).
    /// For write operations, use UpdateSettingsAsync to ensure thread-safe read-modify-write cycles.
    /// </summary>
    /// <returns>A deep copy of the current settings.</returns>
    AppSettings GetSettingsSnapshot();

    /// <summary>
    /// Applies a synchronous patch to the latest settings in a serialized update.
    /// Changes become visible after the file is saved successfully.
    /// </summary>
    /// <param name="updateAction">Action that modifies the settings.</param>
    Task UpdateSettingsAsync(Action<AppSettings> updateAction);
}
