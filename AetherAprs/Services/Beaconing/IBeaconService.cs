// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Models;
using System;
using System.Collections.Generic;

namespace AetherAprs.Services.Beaconing;

/// <summary>
/// Service interface for managing dynamic position beaconing.
/// </summary>
public interface IBeaconService
{
    /// <summary>
    /// Event raised when the beacon service determines a beacon should be transmitted.
    /// </summary>
    event EventHandler<BeaconRequestedEventArgs>? BeaconRequested;

    /// <summary>
    /// Gets the current active beacon configuration based on the selected mode.
    /// </summary>
    BeaconConfig CurrentConfiguration { get; }

    /// <summary>
    /// Gets all three beacon configurations (Walk, Drive, Custom).
    /// </summary>
    IReadOnlyList<BeaconConfig> AllConfigurations { get; }

    /// <summary>
    /// Gets the last calculated course in degrees (0-360), or null if not yet calculated.
    /// </summary>
    double? LastCourseDegrees { get; }

    /// <summary>
    /// Gets the current beacon decision information (updated on each location update).
    /// </summary>
    BeaconTransmitDecision? CurrentDecision { get; }

    /// <summary>
    /// Sets the active beacon mode (Walk, Drive, or Custom).
    /// </summary>
    /// <param name="mode">The beacon mode to activate.</param>
    void SetActiveMode(DynamicBeaconMode mode);

    /// <summary>
    /// Updates the beacon configuration for the mode specified by <see cref="BeaconConfig.Mode"/>.
    /// </summary>
    /// <param name="configuration">The configuration to store for its mode.</param>
    void UpdateConfiguration(BeaconConfig configuration);

    /// <summary>
    /// Processes a location update. Internally evaluates whether to transmit and raises
    /// the BeaconRequested event if conditions are met.
    /// </summary>
    /// <param name="currentLocation">The current location data.</param>
    void ProcessLocationUpdate(LocationData currentLocation);
}

/// <summary>
/// Event arguments for when a beacon should be transmitted.
/// </summary>
public sealed class BeaconRequestedEventArgs : EventArgs
{
    /// <summary>
    /// Gets the location data to beacon.
    /// </summary>
    public required LocationData Location { get; init; }

    /// <summary>
    /// Gets the decision information explaining why the beacon was requested.
    /// </summary>
    public required BeaconTransmitDecision Decision { get; init; }
}

/// <summary>
/// Decision information for whether to transmit a beacon.
/// </summary>
public sealed record BeaconTransmitDecision
{
    /// <summary>
    /// Gets a value indicating whether a beacon should be transmitted.
    /// </summary>
    public bool ShouldTransmit { get; init; }

    /// <summary>
    /// Gets the strongly-typed reason for the decision.
    /// </summary>
    public required BeaconTransmitReason Reason { get; init; }

    /// <summary>
    /// Gets the current speed in km/h, if calculated.
    /// </summary>
    public double? CurrentSpeedKmh { get; init; }

    /// <summary>
    /// Gets the current course in degrees, if calculated.
    /// </summary>
    public double? CurrentCourseDegrees { get; init; }

    /// <summary>
    /// Gets the active beacon interval in seconds.
    /// </summary>
    public int ActiveIntervalSeconds { get; init; }

    /// <summary>
    /// Gets the seconds remaining until the next scheduled beacon.
    /// </summary>
    public int SecondsUntilNextBeacon { get; init; }
}
