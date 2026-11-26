// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Configuration;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AetherAprs.Services.Ports;

/// <summary>
/// Read-only view of the configured ports. Consumers that only need topology
/// (for example a forwarding decision) depend on this, never on the mutable
/// configuration service.
/// </summary>
public interface IPortConfigurationSource
{
    /// <summary>Configured ports as independent copies.</summary>
    IReadOnlyList<PortConfig> GetPorts();

    /// <summary>
    /// Returns a copy of the configured port, or <see langword="null"/> when it is
    /// not configured.
    /// </summary>
    PortConfig? Find(Guid id);
}

/// <summary>
/// Owns the configured ports. Every mutation is durable-first: the store is
/// updated before the in-memory snapshot changes, so a failed write can never
/// leave the runtime believing an unpersisted intent is saved.
/// </summary>
public interface IPortConfigurationService : IPortConfigurationSource
{
    /// <summary>Raised after a mutation changed the configured ports.</summary>
    event EventHandler? PortsChanged;

    /// <summary>Loads the configured ports from the store.</summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>Persists a new port and then publishes it.</summary>
    Task AddAsync(PortConfig port, CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists an existing port and then replaces its in-memory settings.
    /// Returns <see langword="false"/> when the port is no longer configured.
    /// </summary>
    Task<bool> UpdateAsync(PortConfig port, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a configured port. Returns <see langword="false"/> when the port
    /// was not configured.
    /// </summary>
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists the operator's enablement intent and returns the stored snapshot,
    /// or <see langword="null"/> when the port is no longer configured.
    /// </summary>
    Task<PortConfig?> SetEnabledAsync(Guid id, bool enabled, CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists whether the port is drawn on the map and returns the stored
    /// snapshot, or <see langword="null"/> when the port is no longer configured.
    /// </summary>
    Task<PortConfig?> SetShowOnMapAsync(Guid id, bool showOnMap, CancellationToken cancellationToken = default);
}
