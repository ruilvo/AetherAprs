// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Services.Platform;
using System.Threading.Tasks;

namespace AetherAprs.Android.Services.Platform;

/// <summary>
/// Android implementation of permission service.
/// </summary>
public class AndroidPermissionService : IPermissionService
{
    /// <inheritdoc/>
    public async Task<bool> RequestNotificationPermissionAsync()
    {
        var activity = MainActivity.Instance;
        if (activity == null)
        {
            return false;
        }

        return await activity.RequestNotificationPermissionAsync();
    }
}
