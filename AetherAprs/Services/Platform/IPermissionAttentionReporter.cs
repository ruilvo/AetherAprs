// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;

namespace AetherAprs.Services.Platform;

/// <summary>
/// Collects permission outcomes that still need the operator, so a page can show
/// what is blocked and how to fix it.
/// </summary>
/// <remarks>
/// Reporting a granted or unavailable permission clears it again: neither leaves the
/// operator a choice to make, and a warning that cannot be acted on trains people to
/// ignore warnings.
/// </remarks>
public interface IPermissionAttentionReporter
{
    /// <summary>The permissions that currently block part of the application.</summary>
    IReadOnlyList<PermissionAttention> Outstanding { get; }

    /// <summary>Raised when <see cref="Outstanding"/> changes.</summary>
    event EventHandler? Changed;

    /// <summary>Records the outcome of requesting or rechecking one permission.</summary>
    /// <param name="result">The outcome to record.</param>
    void Report(PermissionRequestResult result);
}
