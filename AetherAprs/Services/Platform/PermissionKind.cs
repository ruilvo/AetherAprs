// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

namespace AetherAprs.Services.Platform;

/// <summary>
/// A runtime permission the application may have to request.
/// </summary>
public enum PermissionKind
{
    /// <summary>Permission to post notifications, required before a foreground service may run.</summary>
    Notifications,

    /// <summary>Permission to scan for and connect to Bluetooth devices.</summary>
    Bluetooth,

    /// <summary>Permission to read the device location.</summary>
    Location,

    /// <summary>
    /// Permission to keep receiving location updates while the application is not in
    /// the foreground. On Android 11 and later only the application settings can grant
    /// it, so a refusal is a settings problem rather than something to re-ask.
    /// </summary>
    BackgroundLocation,

    /// <summary>Permission to record audio for AFSK decoding and transmission.</summary>
    Audio,

    /// <summary>
    /// Exemption from battery optimization, so location tracking is not deferred
    /// while the application is in the background.
    /// </summary>
    BatteryOptimization
}
