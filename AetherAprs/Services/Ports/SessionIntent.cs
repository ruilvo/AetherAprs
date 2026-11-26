// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Linq;

namespace AetherAprs.Services.Ports;

/// <summary>
/// The operator's desired running set, kept apart from the runtime status of the
/// ports themselves.
/// </summary>
/// <remarks>
/// Intent is runtime memory on purpose. A restarted process comes up disarmed and
/// waits for an explicit start, so an update, force-stop or process death can never
/// resume transmitting on its own.
/// </remarks>
public sealed class SessionIntent
{
    private SessionIntent(IReadOnlyList<Guid> portIds)
    {
        PortIds = portIds;
    }

    /// <summary>
    /// No port is requested to run.
    /// </summary>
    public static SessionIntent Disarmed { get; } = new(Array.Empty<Guid>());

    /// <summary>
    /// The ports the operator asked to run, ordered and distinct.
    /// </summary>
    public IReadOnlyList<Guid> PortIds { get; }

    public bool IsArmed => PortIds.Count > 0;

    public static SessionIntent Armed(IEnumerable<Guid> portIds)
    {
        ArgumentNullException.ThrowIfNull(portIds);

        var distinct = portIds
            .Distinct()
            .OrderBy(portId => portId)
            .ToArray();

        return distinct.Length == 0 ? Disarmed : new SessionIntent(Array.AsReadOnly(distinct));
    }

    public bool Includes(Guid portId) => PortIds.Contains(portId);
}
