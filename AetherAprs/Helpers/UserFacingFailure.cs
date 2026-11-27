// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Localization;
using AetherAprs.Models.Objects;
using System;
using System.IO;

namespace AetherAprs.Helpers;

/// <summary>
/// Classifies the exceptions a feature service or platform adapter can raise into
/// the categories the operator can actually act on.
/// </summary>
/// <remarks>
/// A view model must not put <see cref="Exception.Message"/> on screen: the text is
/// written for a developer, it is English only, and it can carry file paths or frame
/// data. The category names what happened, so the operator learns which of the few
/// recovery actions applies — connect a route, grant a permission, free storage — and
/// nothing else leaks into the UI.
/// </remarks>
public static class UserFacingFailure
{
    /// <summary>
    /// Maps a failure to its localized description.
    /// </summary>
    /// <param name="exception">The failure to describe.</param>
    /// <returns>A localized sentence the operator can act on.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="exception"/> is null.</exception>
    public static string Describe(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        // A typed broadcast failure already knows which reason to show, and that
        // reason is more specific than the generic category its base type maps to.
        return exception is BroadcastFailedException broadcast
            ? BroadcastFailureText.Describe(broadcast.Failure)
            : Strings.Get(Category(exception));
    }

    /// <summary>
    /// Gets the resource key for the category <paramref name="exception"/> belongs to.
    /// </summary>
    /// <param name="exception">The failure to categorise.</param>
    /// <returns>The localization key, never null.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="exception"/> is null.</exception>
    public static string Category(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return exception switch
        {
            // A typed broadcast failure carries its own reason; Describe uses it.
            BroadcastFailedException => "FailureUnavailable",

            // The platform refused a runtime permission: only the operator can fix it.
            UnauthorizedAccessException => "FailurePermissionBlocked",

            // A negotiation or connection attempt failed, or no route can carry the work.
            InvalidOperationException => "FailureUnavailable",

            // The volume is full, read-only, or the file is locked by something else.
            IOException => "FailureStorageUnavailable",

            OperationCanceledException => "FailureCancelled",

            // Anything else is reported generically: the operator cannot tell one
            // unexpected provider failure from another, and the detail is in the log.
            _ => "FailureUnexpected",
        };
    }
}
