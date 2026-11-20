// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Threading.Tasks;

namespace AetherAprs.Services.Platform;

/// <summary>
/// Platform-specific service for requesting runtime permissions.
/// </summary>
public interface IPermissionService
{
    /// <summary>
    /// Requests notification permission if required by the platform.
    /// </summary>
    /// <returns>True if permission is granted or not required, false if denied.</returns>
    Task<bool> RequestNotificationPermissionAsync();

    /// <summary>
    /// Requests Bluetooth permissions if required by the platform.
    /// </summary>
    /// <returns>True if permission is granted or not required, false if denied.</returns>
    Task<bool> RequestBluetoothPermissionAsync();

    /// <summary>
    /// Requests location permissions if required by the platform.
    /// </summary>
    /// <returns>True if permission is granted or not required, false if denied.</returns>
    Task<bool> RequestLocationPermissionAsync();

    /// <summary>
    /// Requests microphone (audio recording) permission if required by the platform.
    /// </summary>
    /// <returns>True if permission is granted or not required, false if denied.</returns>
    Task<bool> RequestAudioPermissionAsync();

    /// <summary>
    /// Requests exemption from battery optimization (Doze/App Standby) so background
    /// tracking is not deferred or killed. Only meaningful on Android.
    /// </summary>
    /// <returns>True if already exempt or not required, false if the user prompt was shown.</returns>
    Task<bool> RequestIgnoreBatteryOptimizationsAsync();
}
