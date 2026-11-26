// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Threading;
using System.Threading.Tasks;

namespace AetherAprs.Services.Platform;

/// <summary>
/// Platform-specific service for checking and requesting runtime permissions.
/// </summary>
public interface IPermissionService
{
    /// <summary>
    /// Checks the permission, prompting the operator when the platform still allows a
    /// prompt. A granted permission and a permission the platform does not require
    /// both report <see cref="PermissionOutcome.Granted"/>.
    /// </summary>
    /// <param name="kind">The permission to check or request.</param>
    /// <param name="cancellationToken">Cancels waiting for the operator's answer.</param>
    /// <returns>The outcome with the recovery action it allows.</returns>
    Task<PermissionRequestResult> RequestAsync(
        PermissionKind kind,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens this application's settings page so the operator can grant a permission
    /// the platform will no longer prompt for.
    /// </summary>
    /// <param name="cancellationToken">Cancels launching the settings page.</param>
    /// <returns>True when the settings page was opened.</returns>
    Task<bool> OpenApplicationSettingsAsync(CancellationToken cancellationToken = default);
}
