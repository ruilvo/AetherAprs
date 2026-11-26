// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

namespace AetherAprs.Services.Platform;

/// <summary>
/// What the platform currently says about a permission, without asking the
/// operator anything.
/// </summary>
public enum PermissionGrantStatus
{
    /// <summary>The permission is currently granted.</summary>
    Granted,

    /// <summary>The permission is currently withheld.</summary>
    Denied,

    /// <summary>
    /// The platform has no runtime grant to report: the permission does not exist
    /// at this platform level, or the host cannot answer right now.
    /// </summary>
    Unsupported
}
