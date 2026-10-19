// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Models;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace AetherAprs.Services.Platform;

/// <summary>
/// Safe default implementation of ILocationService for platforms without
/// location support. Location is always reported unavailable so consumers
/// (e.g. LocationTrackingService) skip tracking entirely.
/// </summary>
public sealed class NoOpLocationService : ILocationService
{
    /// <inheritdoc/>
    public Task<LocationData> GetCurrentLocationAsync(CancellationToken cancellationToken = default)
    {
        throw new InvalidOperationException("Location services are not available on this platform.");
    }

    /// <inheritdoc/>
    public bool IsLocationAvailable() => false;

    /// <inheritdoc/>
    public Task<bool> RequestLocationPermissionAsync() => Task.FromResult(false);
}
