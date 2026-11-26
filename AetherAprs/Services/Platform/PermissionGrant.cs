// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

namespace AetherAprs.Services.Platform;

/// <summary>
/// A non-prompting answer about one permission.
/// </summary>
/// <param name="Status">The current grant state.</param>
/// <remarks>
/// Reading a grant is not the same as requesting it: the answer can come from a
/// resumed activity, a settings screen the operator just left, or a system dialog
/// that answered on its own. Nothing here is allowed to show a prompt.
/// </remarks>
public readonly record struct PermissionGrant(PermissionGrantStatus Status)
{
    /// <summary>A grant that is currently held.</summary>
    public static PermissionGrant Granted { get; } = new(PermissionGrantStatus.Granted);

    /// <summary>A grant that is currently withheld.</summary>
    public static PermissionGrant Denied { get; } = new(PermissionGrantStatus.Denied);

    /// <summary>No runtime grant exists to report.</summary>
    public static PermissionGrant Unsupported { get; } = new(PermissionGrantStatus.Unsupported);
}
