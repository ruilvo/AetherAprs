// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Collections.Generic;
using AetherAprs.Models;
using AetherAprs.Models.Aprs;

namespace AetherAprs.Services;

/// <summary>
/// Service interface for managing dynamic position beaconing.
/// </summary>
public interface IBeaconService
{
    /// <summary>
    /// Gets the current active beacon configuration based on the selected mode.
    /// </summary>
    BeaconConfig CurrentConfiguration { get; }

    /// <summary>
    /// Gets all three beacon configurations (Walk, Drive, Custom).
    /// </summary>
    IReadOnlyList<BeaconConfig> AllConfigurations { get; }

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
    /// Processes a location update and determines if a beacon should be transmitted.
    /// </summary>
    /// <param name="currentLocation">The current location data.</param>
    /// <param name="previousLocation">The previous location data, if available.</param>
    /// <returns>BeaconTransmitDecision indicating whether to transmit and why.</returns>
    BeaconTransmitDecision EvaluateLocationUpdate(LocationData currentLocation, LocationData? previousLocation);

    /// <summary>
    /// Resets the beacon transmission timer. Call this after successfully transmitting a beacon.
    /// </summary>
    void ResetTransmissionTimer();

    /// <summary>
    /// Creates a position packet for transmission based on current beacon configuration.
    /// </summary>
    /// <param name="location">The location to beacon.</param>
    /// <param name="callsign">The callsign to use in the packet.</param>
    /// <param name="symbolTableCharacter">The APRS symbol table character (default "/").</param>
    /// <param name="symbolCodeCharacter">The APRS symbol code character (default "[").</param>
    /// <returns>A PositionPacket ready for transmission.</returns>
    PositionPacket CreatePositionPacket(
        LocationData location,
        string callsign,
        string symbolTableCharacter = "/",
        string symbolCodeCharacter = "[");
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
    /// Gets the reason for the decision (e.g., "Course changed 25°", "Time interval exceeded").
    /// </summary>
    public string Reason { get; init; } = string.Empty;

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
