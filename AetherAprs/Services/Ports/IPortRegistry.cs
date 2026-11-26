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
/// Durable port configuration. Every mutation is persisted before the caller
/// observes success, and failures leave the last committed state intact.
/// </summary>
/// <remarks>
/// Implementations own password protection and the storage schema; callers pass
/// domain configuration and receive the authoritative stored snapshot back.
/// </remarks>
public interface IPortRegistry
{
    /// <summary>Loads every configured port.</summary>
    Task<IReadOnlyList<PortConfig>> LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>Inserts or replaces <paramref name="port"/> and returns the stored configuration.</summary>
    Task<IReadOnlyList<PortConfig>> SaveAsync(PortConfig port, CancellationToken cancellationToken = default);

    /// <summary>Removes the port with <paramref name="id"/> and returns the remaining configuration.</summary>
    Task<IReadOnlyList<PortConfig>> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
