// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;

namespace AetherAprs.Services;

/// <summary>
/// Default implementation of IAppDataDirProviderService for desktop/core platforms.
/// Returns the application's base directory.
/// </summary>
public class AppDataDirProviderService : IAppDataDirProviderService
{
    /// <inheritdoc/>
    public string GetAppDataDirectory()
    {
        return AppContext.BaseDirectory;
    }
}
