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
        // SUPPRESSED WARNING CA1422: GetExternalMediaDirs() is obsoleted on Android API 30+
        // This method is still functional but deprecated. The Android documentation doesn't provide
        // a clear replacement for media directories. The method continues to work on API 30+ devices.
        // LONG-TERM SOLUTION: Evaluate using Android's scoped storage APIs (MediaStore) for user-accessible
        // files, or migrate to getExternalFilesDir() as the primary location if media directory access
        // is not strictly required. This requires analyzing app requirements for file accessibility.
#pragma warning disable CA1422
        var mediaDir = global::Android.App.Application.Context.GetExternalMediaDirs()?.FirstOrDefault();
#pragma warning restore CA1422
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
        // SUPPRESSED WARNING CS8602: Possible null reference dereference
        // Context.FilesDir is documented to never return null in normal app lifecycle.
        // This is a last-resort fallback that should never fail unless the app is in an invalid state.
        // LONG-TERM SOLUTION: Add proper null handling with a meaningful exception if FilesDir is null,
        // which would indicate a critical system failure requiring app termination or error reporting.
#pragma warning disable CS8602
        return global::Android.App.Application.Context.FilesDir.AbsolutePath;
#pragma warning restore CS8602
    }
}
