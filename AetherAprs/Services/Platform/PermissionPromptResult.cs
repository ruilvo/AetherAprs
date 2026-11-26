// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

namespace AetherAprs.Services.Platform;

/// <summary>
/// The result of asking the platform to show a permission prompt.
/// </summary>
public enum PermissionPromptResult
{
    /// <summary>
    /// The operator granted the permission, or it was already granted.
    /// </summary>
    Granted,

    /// <summary>
    /// The operator declined the prompt. Whether they can be asked again is
    /// decided by <see cref="PermissionRequestPolicy"/>.
    /// </summary>
    Denied,

    /// <summary>
    /// No answer will arrive: the prompt host is gone, the request timed out, or
    /// the application is shutting down. This is distinct from a denial because
    /// nothing may be recorded about the operator's choice.
    /// </summary>
    Abandoned,
}
