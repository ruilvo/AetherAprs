// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;

namespace AetherAprs.Services.Platform;

/// <summary>
/// Applies the outcome of a resume-time grant recheck to the record the operator
/// sees.
/// </summary>
/// <remarks>
/// <para>
/// A grant can be revoked or restored while the application is out of the
/// foreground, and a resumed activity re-reads it without prompting. The recheck
/// only logs by itself, so without this step the home banner kept whatever it was
/// told at the last prompt: revoking a permission in the system settings left no
/// warning, and restoring it left a stale one.
/// </para>
/// <para>
/// The rule is kept here rather than in the Android entry point because it is pure
/// bookkeeping: the caller only has to supply the transitions and the reporter.
/// </para>
/// </remarks>
public static class PermissionResumeReport
{
    /// <summary>
    /// Reports every transition that still needs the operator and clears the ones
    /// they resolved.
    /// </summary>
    /// <param name="transitions">The outcomes observed by the recheck.</param>
    /// <param name="attention">
    /// The record the operator-facing banner reads, or <see langword="null"/> when
    /// the application has none.
    /// </param>
    /// <returns>The transitions that changed, in the order they were observed.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="transitions"/> is null.
    /// </exception>
    public static IReadOnlyList<PermissionGrantTransition> Apply(
        IReadOnlyList<PermissionGrantTransition> transitions,
        IPermissionAttentionReporter? attention)
    {
        ArgumentNullException.ThrowIfNull(transitions);

        List<PermissionGrantTransition> changed = [];
        foreach (PermissionGrantTransition transition in transitions)
        {
            if (!transition.HasChanged)
            {
                // A permission read for the first time has no previous outcome. It is
                // not a state the operator caused, and warning about it would flag a
                // feature they may not even use.
                continue;
            }

            changed.Add(transition);

            // Report raises the banner when the new outcome still blocks the
            // operator, and clears the entry when a grant or an unsupported
            // permission leaves them nothing to do.
            attention?.Report(transition.Result);
        }

        return changed;
    }
}
