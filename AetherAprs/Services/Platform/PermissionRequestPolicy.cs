// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

namespace AetherAprs.Services.Platform;

/// <summary>
/// Decides how a permission check maps onto <see cref="PermissionOutcome"/>.
/// </summary>
/// <remarks>
/// Android reports "should show rationale" as false both before the first request
/// and after a permanent denial, so the flag alone cannot identify a permanent
/// denial. The caller supplies whether an earlier request was made; only then does
/// a missing rationale mean the prompt will not appear again.
/// </remarks>
public static class PermissionRequestPolicy
{
    /// <summary>
    /// Evaluates the platform facts for one permission.
    /// </summary>
    /// <param name="kind">The permission that was checked.</param>
    /// <param name="granted">Whether the platform reports the permission as granted.</param>
    /// <param name="rationaleSuggested">Whether the platform suggests explaining before prompting.</param>
    /// <param name="requestedBefore">
    /// Whether a request for this permission was already made in this process.
    /// </param>
    /// <param name="supported">
    /// Whether the running platform level can provide this permission at all.
    /// </param>
    /// <returns>The outcome, including the recovery action it allows.</returns>
    public static PermissionRequestResult Evaluate(
        PermissionKind kind,
        bool granted,
        bool rationaleSuggested,
        bool requestedBefore,
        bool supported = true)
    {
        if (!supported)
        {
            return PermissionRequestResult.Unavailable(kind);
        }

        if (granted)
        {
            return PermissionRequestResult.Granted(kind);
        }

        // The prompt will be shown again, so a plain denial is enough. Before the
        // first request the flag is also false, which is why the "requested before"
        // fact decides the remaining cases instead of the flag alone.
        if (rationaleSuggested || !requestedBefore)
        {
            return PermissionRequestResult.Denied(kind);
        }

        return PermissionRequestResult.PermanentlyDenied(kind);
    }
}
