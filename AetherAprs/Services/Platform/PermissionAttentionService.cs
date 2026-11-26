// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace AetherAprs.Services.Platform;

/// <summary>
/// In-memory record of the permissions that still need the operator.
/// </summary>
public sealed class PermissionAttentionService : IPermissionAttentionReporter
{
    private readonly Lock _gate = new();
    private readonly Dictionary<PermissionKind, PermissionOutcome> _outstanding = [];

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <inheritdoc />
    public IReadOnlyList<PermissionAttention> Outstanding
    {
        get
        {
            lock (_gate)
            {
                return [.. _outstanding
                    .OrderBy(entry => entry.Key)
                    .Select(entry => new PermissionAttention(entry.Key, entry.Value))];
            }
        }
    }

    /// <inheritdoc />
    public void Report(PermissionRequestResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        bool changed;
        lock (_gate)
        {
            changed = result.Outcome is PermissionOutcome.Denied or PermissionOutcome.PermanentlyDenied
                ? Record(result)
                : _outstanding.Remove(result.Kind);
        }

        // Raise outside the lock so a handler cannot deadlock against a concurrent
        // report.
        if (changed)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }

        bool Record(PermissionRequestResult reported) =>
            (!_outstanding.TryGetValue(reported.Kind, out PermissionOutcome previous) || previous != reported.Outcome) && Set(reported);

        bool Set(PermissionRequestResult reported)
        {
            _outstanding[reported.Kind] = reported.Outcome;
            return true;
        }
    }
}
