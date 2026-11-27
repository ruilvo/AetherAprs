// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;

namespace AetherAprs.Android.Services.Platform;

/// <summary>
/// Resolves the drawable resource name carried by a
/// <see cref="NotificationContentSpec"/> to the generated resource id.
/// </summary>
internal static class AndroidNotificationIcons
{
    /// <summary>Resolves a notification small-icon resource name.</summary>
    /// <param name="resourceName">Name from <see cref="NotificationContentSpec.IconResourceName"/>.</param>
    /// <exception cref="ArgumentOutOfRangeException">The name is unknown.</exception>
    public static int Resolve(string resourceName) => resourceName switch
    {
        NotificationSpecs.NotificationIconResourceName => Resource.Drawable.ic_notification,
        _ => throw new ArgumentOutOfRangeException(
            nameof(resourceName),
            resourceName,
            "Unknown notification icon resource.")
    };
}
