// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later
using AetherAprs.Configuration;
using System.Threading.Tasks;

namespace AetherAprs.Services;

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
}
