// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Collections.Generic;

namespace AetherAprs.Services.Platform;

/// <summary>
/// Re-reads every relevant permission and reports what changed since the last
/// read.
/// </summary>
/// <remarks>
/// The platform entry point calls this when it becomes visible again. A grant can
/// be revoked or restored while the application is in the background, and the
/// application has to notice without prompting the operator a second time.
/// </remarks>
public interface IPermissionGrantRechecker
{
    /// <summary>
    /// Reads every relevant permission and evaluates the current grants.
    /// </summary>
    /// <returns>One entry per permission read, in a stable order.</returns>
    IReadOnlyList<PermissionGrantTransition> Recheck();
}
