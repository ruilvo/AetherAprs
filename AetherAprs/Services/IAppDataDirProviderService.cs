// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

namespace AetherAprs.Services;

/// <summary>
/// Provides platform-specific application data directory paths.
/// </summary>
public interface IAppDataDirProviderService
{
    /// <summary>
    /// Gets the platform-specific application data directory path.
    /// </summary>
    /// <returns>The absolute path to the application data directory.</returns>
    string GetAppDataDirectory();
}
