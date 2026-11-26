// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Threading;
using System.Threading.Tasks;

namespace AetherAprs.Services.Platform;

/// <summary>
/// Safe default implementation of IPermissionService for platforms that do
/// not require runtime permissions. Every permission reports "granted or not
/// required" so permission-gated flows proceed on such platforms.
/// </summary>
public sealed class NoOpPermissionService : IPermissionService
{
    /// <inheritdoc/>
    public Task<PermissionRequestResult> RequestAsync(
        PermissionKind kind,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(PermissionRequestResult.Granted(kind));

    /// <inheritdoc/>
    public Task<bool> OpenApplicationSettingsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(false);
}
