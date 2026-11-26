// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

namespace AetherAprs.Services.Platform;

/// <summary>
/// The outcome of checking or requesting a runtime permission. The states are kept
/// apart because the recovery action differs: a denial can be retried with a prompt,
/// a permanent denial can only be fixed in the application settings, and an
/// unavailable permission must not be requested again at all.
/// </summary>
public enum PermissionOutcome
{
    /// <summary>The permission is granted, or the platform does not require it.</summary>
    Granted,

    /// <summary>The permission is denied but the platform will show the prompt again.</summary>
    Denied,

    /// <summary>
    /// The permission is denied and will not be prompted again; the operator has to
    /// grant it in the application settings.
    /// </summary>
    PermanentlyDenied,

    /// <summary>The platform cannot provide this permission, so nothing is requested.</summary>
    Unavailable
}
