// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Threading;

using Microsoft.Extensions.Logging;

namespace AetherAprs.Services.Platform;

/// <summary>
/// Records the outcome of non-prompting grant reads and reports transitions.
/// </summary>
/// <remarks>
/// The same facts that decide a prompt's outcome decide a recheck's outcome, so
/// both go through <see cref="PermissionRequestPolicy"/>. What differs is the
/// missing rationale signal: a resumed application cannot tell a permanent denial
/// from a first-time denial by prompting, so the caller supplies the same
/// "rationale suggested" and "requested before" facts it would have used for a
/// prompt. Only an actual change is logged and raised, so a resume that observes
/// nothing new stays quiet.
/// </remarks>
/// <remarks>
/// Initializes a tracker.
/// </remarks>
/// <param name="logger">An optional logger for observed transitions.</param>
public sealed class PermissionGrantTracker(ILogger<PermissionGrantTracker>? logger = null)
{
    private readonly Lock _gate = new();
    private readonly Dictionary<PermissionKind, PermissionOutcome> _lastKnown = [];
    private readonly ILogger<PermissionGrantTracker>? _logger = logger;

    /// <summary>
    /// Raised once per permission whose evaluated outcome differs from the one
    /// recorded before the read.
    /// </summary>
    public event EventHandler<PermissionGrantTransition>? Changed;

    /// <summary>
    /// Evaluates and records one current grant.
    /// </summary>
    /// <param name="kind">The permission that was read.</param>
    /// <param name="grant">The grant the platform reported.</param>
    /// <param name="rationaleSuggested">
    /// Whether the platform would still show a rationale, which distinguishes a
    /// denial the operator can be asked about again from a permanent one.
    /// </param>
    /// <param name="requestedBefore">
    /// Whether this process already asked for the permission.
    /// </param>
    /// <returns>The evaluated outcome of the current grant and the transition.</returns>
    public PermissionGrantTransition Observe(
        PermissionKind kind,
        PermissionGrant grant,
        bool rationaleSuggested,
        bool requestedBefore)
    {
        PermissionRequestResult result = PermissionRequestPolicy.Evaluate(
            kind,
            granted: grant.Status == PermissionGrantStatus.Granted,
            rationaleSuggested,
            requestedBefore,
            supported: grant.Status != PermissionGrantStatus.Unsupported);

        PermissionGrantTransition transition;
        lock (_gate)
        {
            bool hadPrevious = _lastKnown.TryGetValue(kind, out PermissionOutcome previous);
            _lastKnown[kind] = result.Outcome;

            transition = new PermissionGrantTransition(kind, hadPrevious ? previous : null, result);
        }

        if (transition.HasChanged)
        {
            _logger?.LogInformation(
                "Permission {Permission} changed from {Previous} to {Current}",
                kind,
                transition.Previous?.ToString() ?? "unknown",
                result.Outcome);

            Changed?.Invoke(this, transition);
        }

        return transition;
    }

    /// <summary>
    /// Gets the outcome recorded for <paramref name="kind"/>, if it was ever read.
    /// </summary>
    /// <param name="kind">The permission to look up.</param>
    /// <returns>The recorded outcome, or <see langword="null"/>.</returns>
    public PermissionOutcome? LastKnown(PermissionKind kind)
    {
        lock (_gate)
        {
            return _lastKnown.TryGetValue(kind, out PermissionOutcome outcome) ? outcome : null;
        }
    }
}
