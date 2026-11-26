// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AetherAprs.Services.Ports;

/// <summary>
/// Authoritative in-memory port configuration backed by <see cref="IPortRegistry"/>.
/// Runtime convergence (starting or stopping sessions) belongs to the caller; this
/// type only guarantees that persistence and the published snapshot agree.
/// </summary>
internal sealed class PortConfigurationService(IPortRegistry portRegistry) : IPortConfigurationService
{
    // Guards _ports. Session state is owned and guarded by the session manager.
    private readonly Lock _stateLock = new();
    private readonly List<PortConfig> _ports = [];

    public event EventHandler? PortsChanged;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<PortConfig> loaded = await portRegistry.LoadAsync(cancellationToken).ConfigureAwait(false);

        lock (_stateLock)
        {
            _ports.Clear();
            _ports.AddRange(loaded.Select(port => port.Clone()));
        }

        RaisePortsChanged();
    }

    public IReadOnlyList<PortConfig> GetPorts()
    {
        lock (_stateLock)
        {
            // Hand out copies: a caller must never be able to mutate the
            // authoritative state by editing a published snapshot.
            return [.. _ports.Select(port => port.Clone())];
        }
    }

    public PortConfig? Find(Guid id)
    {
        lock (_stateLock)
        {
            return _ports.FirstOrDefault(port => port.Id == id)?.Clone();
        }
    }

    public async Task AddAsync(PortConfig port, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(port);

        // Durable-first: never expose a port in memory that was not saved.
        await portRegistry.SaveAsync(port, cancellationToken).ConfigureAwait(false);

        lock (_stateLock)
        {
            _ports.Add(port.Clone());
        }

        RaisePortsChanged();
    }

    public async Task<bool> UpdateAsync(PortConfig port, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(port);

        if (Find(port.Id) is null)
        {
            return false;
        }

        // Durable-first: the persisted row must match the new settings before the
        // in-memory snapshot is replaced.
        await portRegistry.SaveAsync(port, cancellationToken).ConfigureAwait(false);

        lock (_stateLock)
        {
            PortConfig? stored = _ports.FirstOrDefault(candidate => candidate.Id == port.Id);
            if (stored is null)
            {
                return false;
            }

            CopyPortProperties(port, stored);
        }

        RaisePortsChanged();
        return true;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (Find(id) is null)
        {
            return false;
        }

        // Durable-first: the row must be gone before the port disappears from the
        // in-memory snapshot.
        await portRegistry.DeleteAsync(id, cancellationToken).ConfigureAwait(false);

        lock (_stateLock)
        {
            PortConfig? stored = _ports.FirstOrDefault(port => port.Id == id);
            if (stored is not null)
            {
                _ports.Remove(stored);
            }
        }

        RaisePortsChanged();
        return true;
    }

    public async Task<PortConfig?> SetEnabledAsync(
        Guid id,
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        PortConfig? current = Find(id);
        if (current is null)
        {
            return null;
        }

        if (current.IsEnabled == enabled)
        {
            return current;
        }

        PortConfig desired = current.WithEnabled(enabled);
        await portRegistry.SaveAsync(desired, cancellationToken).ConfigureAwait(false);

        lock (_stateLock)
        {
            PortConfig? stored = _ports.FirstOrDefault(port => port.Id == id);
            stored?.IsEnabled = enabled;
        }

        RaisePortsChanged();
        return Find(id);
    }

    public async Task<PortConfig?> SetShowOnMapAsync(
        Guid id,
        bool showOnMap,
        CancellationToken cancellationToken = default)
    {
        PortConfig? current = Find(id);
        if (current is null)
        {
            return null;
        }

        if (current.ShowOnMap == showOnMap)
        {
            return current;
        }

        PortConfig desired = current.WithShowOnMap(showOnMap);
        await portRegistry.SaveAsync(desired, cancellationToken).ConfigureAwait(false);

        lock (_stateLock)
        {
            PortConfig? stored = _ports.FirstOrDefault(port => port.Id == id);
            stored?.ShowOnMap = showOnMap;
        }

        RaisePortsChanged();
        return Find(id);
    }

    private void RaisePortsChanged() => PortsChanged?.Invoke(this, EventArgs.Empty);

    private static void CopyPortProperties(PortConfig source, PortConfig target)
    {
        target.Name = source.Name;
        target.IsEnabled = source.IsEnabled;
        target.IsRx = source.IsRx;
        target.IsTx = source.IsTx;
        target.ShowOnMap = source.ShowOnMap;
        target.AllowDigipeat = source.AllowDigipeat;
        // Copy the settings graph too: the stored instance must never alias the
        // caller's, or a later edit elsewhere would silently change live config.
        target.TypeSettings = source.TypeSettings?.Clone();
    }
}
