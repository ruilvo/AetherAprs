// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Linq;

namespace AetherAprs.Android.Services;

/// <summary>
/// Android-specific implementation of IAppDataDirProviderService.
/// Uses external media directory for user-accessible configuration files.
/// </summary>
public class AppDataDirProviderService : AetherAprs.Services.IAppDataDirProviderService
{
    /// <inheritdoc/>
    public string GetAppDataDirectory()
    {
        // Try to use external media directory first (user-accessible: /Android/media/package/)
        var mediaDir = global::Android.App.Application.Context.GetExternalMediaDirs()?.FirstOrDefault();
        if (mediaDir != null)
        {
            return mediaDir.AbsolutePath;
        }

        // Fallback to external files directory
        var externalFilesDir = global::Android.App.Application.Context.GetExternalFilesDir(null);
        if (externalFilesDir != null)
        {
            return externalFilesDir.AbsolutePath;
        }

        // Last resort: internal storage
        return global::Android.App.Application.Context.FilesDir.AbsolutePath;
    }
}
