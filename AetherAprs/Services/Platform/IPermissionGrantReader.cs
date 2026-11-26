// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

namespace AetherAprs.Services.Platform;

/// <summary>
/// Reads the current grant state of a permission without prompting the operator.
/// </summary>
/// <remarks>
/// A resumed activity must be able to notice that the operator revoked a grant in
/// the system settings, or granted one there, without showing a dialog again.
/// </remarks>
public interface IPermissionGrantReader
{
    /// <summary>
    /// Reads the current grant for <paramref name="kind"/>.
    /// </summary>
    /// <param name="kind">The permission to read.</param>
    /// <returns>The grant state, or <see cref="PermissionGrant.Unsupported"/>.</returns>
    PermissionGrant Read(PermissionKind kind);
}
