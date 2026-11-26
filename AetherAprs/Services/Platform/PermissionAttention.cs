// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

namespace AetherAprs.Services.Platform;

/// <summary>
/// A permission that currently keeps part of the application from working.
/// </summary>
/// <param name="Kind">The permission the operator has to act on.</param>
/// <param name="Outcome">Why it is blocking: denied, or blocked in settings.</param>
public sealed record PermissionAttention(PermissionKind Kind, PermissionOutcome Outcome)
{
    /// <summary>
    /// Gets a value indicating whether only the application settings can grant the
    /// permission again, because the platform will not show a dialog any more.
    /// </summary>
    public bool RequiresApplicationSettings => Outcome == PermissionOutcome.PermanentlyDenied;
}
