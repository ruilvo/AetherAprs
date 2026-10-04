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
}
