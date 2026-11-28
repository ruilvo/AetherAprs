// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Threading;
using System.Threading.Tasks;

using AetherAprs.Models;

namespace AetherAprs.Services.Beaconing;

/// <summary>
/// Durable beacon configuration and selected mode. Mutations are persisted before
/// the caller observes success.
/// </summary>
public interface IBeaconStateStore
{
    /// <summary>Loads the stored presets and the last selected mode.</summary>
    Task<BeaconStateSnapshot> LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>Persists <paramref name="configuration"/> and returns the stored snapshot.</summary>
    Task<BeaconStateSnapshot> SaveConfigurationAsync(BeaconConfig configuration, CancellationToken cancellationToken = default);

    /// <summary>Persists the selected mode and returns the stored snapshot.</summary>
    Task<BeaconStateSnapshot> SaveActiveModeAsync(DynamicBeaconMode mode, CancellationToken cancellationToken = default);
}

/// <summary>The beacon presets and selected mode as loaded from durable storage.</summary>
public sealed record BeaconStateSnapshot(
    BeaconConfig Walk,
    BeaconConfig Drive,
    BeaconConfig Custom,
    DynamicBeaconMode ActiveMode)
{
    /// <summary>Returns the configuration for <paramref name="mode"/>.</summary>
    public BeaconConfig For(DynamicBeaconMode mode) => mode switch
    {
        DynamicBeaconMode.Walk => Walk,
        DynamicBeaconMode.Drive => Drive,
        DynamicBeaconMode.Custom => Custom,
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown beacon mode.")
    };
}
