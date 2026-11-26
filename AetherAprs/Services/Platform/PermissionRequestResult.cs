// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;

namespace AetherAprs.Services.Platform;

/// <summary>
/// The outcome of a permission request, together with the permission it describes.
/// </summary>
/// <param name="Kind">The permission that was requested.</param>
/// <param name="Outcome">What the platform reported for that permission.</param>
public sealed record PermissionRequestResult(PermissionKind Kind, PermissionOutcome Outcome)
{
    /// <summary>
    /// Gets a value indicating whether the permission is granted or not required.
    /// </summary>
    public bool IsGranted => Outcome == PermissionOutcome.Granted;

    /// <summary>
    /// Gets a value indicating whether asking again can show the system prompt.
    /// </summary>
    public bool CanPromptAgain => Outcome == PermissionOutcome.Denied;

    /// <summary>
    /// Gets a value indicating whether only the application settings can grant the
    /// permission from now on.
    /// </summary>
    public bool RequiresApplicationSettings => Outcome == PermissionOutcome.PermanentlyDenied;

    public static PermissionRequestResult Granted(PermissionKind kind) =>
        new(kind, PermissionOutcome.Granted);

    public static PermissionRequestResult Denied(PermissionKind kind) =>
        new(kind, PermissionOutcome.Denied);

    public static PermissionRequestResult PermanentlyDenied(PermissionKind kind) =>
        new(kind, PermissionOutcome.PermanentlyDenied);

    public static PermissionRequestResult Unavailable(PermissionKind kind) =>
        new(kind, PermissionOutcome.Unavailable);
}
