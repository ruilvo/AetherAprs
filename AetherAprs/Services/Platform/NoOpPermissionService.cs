// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Threading.Tasks;

namespace AetherAprs.Services.Platform;

/// <summary>
/// Safe default implementation of IPermissionService for platforms that do
/// not require runtime permissions. Every request reports "granted or not
/// required" so permission-gated flows proceed on such platforms.
/// </summary>
public sealed class NoOpPermissionService : IPermissionService
{
    /// <inheritdoc/>
    public Task<bool> RequestNotificationPermissionAsync() => Task.FromResult(true);

    /// <inheritdoc/>
    public Task<bool> RequestBluetoothPermissionAsync() => Task.FromResult(true);

    /// <inheritdoc/>
    public Task<bool> RequestLocationPermissionAsync() => Task.FromResult(true);
}
