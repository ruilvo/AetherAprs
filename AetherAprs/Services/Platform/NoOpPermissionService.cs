// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Threading.Tasks;

namespace AetherAprs.Services.Platform;

/// <summary>
/// No-op permission service for platforms that don't require runtime permissions.
/// </summary>
public class NoOpPermissionService : IPermissionService
{
    /// <inheritdoc/>
    public Task<bool> RequestNotificationPermissionAsync()
    {
        // Desktop platforms don't require notification permission
        return Task.FromResult(true);
    }
}
