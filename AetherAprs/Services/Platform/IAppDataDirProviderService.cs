// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

namespace AetherAprs.Services.Platform;

/// <summary>
/// Provides platform-specific application data directory paths.
/// </summary>
public interface IAppDataDirProviderService
{
    /// <summary>
    /// Gets the platform-specific application data directory path.
    /// May be user-accessible storage (e.g. external media on Android); only
    /// credential-free files such as configuration and caches belong here.
    /// </summary>
    /// <returns>The absolute path to the application data directory.</returns>
    string GetAppDataDirectory();

    /// <summary>
    /// Gets a private application data directory path that is not accessible to
    /// the user or other apps. Sensitive data (e.g. the SQLite database holding
    /// APRS-IS passcodes) must be stored here.
    /// </summary>
    /// <returns>The absolute path to the private application data directory.</returns>
    string GetPrivateAppDataDirectory();
}
