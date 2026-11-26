// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

namespace AetherAprs.Services.Platform;

/// <summary>
/// One observed grant state for a permission and what it means for callers.
/// </summary>
/// <param name="Kind">The permission that was read.</param>
/// <param name="Previous">
/// The outcome recorded for this permission before the read, or
/// <see langword="null"/> when it was never read.
/// </param>
/// <param name="Result">The evaluated outcome of the current grant.</param>
public readonly record struct PermissionGrantTransition(
    PermissionKind Kind,
    PermissionOutcome? Previous,
    PermissionRequestResult Result)
{
    /// <summary>
    /// Gets a value indicating whether the outcome differs from the one recorded
    /// before this read. A permission read for the first time has no previous
    /// outcome, so it is not a transition.
    /// </summary>
    public bool HasChanged => Previous is not null && Previous != Result.Outcome;
}
