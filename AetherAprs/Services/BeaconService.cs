// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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
    BeaconConfiguration CurrentConfiguration { get; }

    /// <summary>
    /// Gets all three beacon configurations (Walk, Drive, Custom).
    /// </summary>
    IReadOnlyList<BeaconConfiguration> AllConfigurations { get; }

    /// <summary>
    /// Sets the active beacon mode (Walk, Drive, or Custom).
    /// </summary>
    /// <param name="mode">The beacon mode to activate.</param>
    void SetActiveMode(DynamicBeaconMode mode);

    /// <summary>
    /// Updates the custom beacon configuration.
    /// </summary>
    /// <param name="configuration">The new custom configuration.</param>
    void UpdateCustomConfiguration(BeaconConfiguration configuration);

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
    /// <returns>A PositionPacket ready for transmission.</returns>
    PositionPacket CreatePositionPacket(LocationData location, string callsign);
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

/// <summary>
/// Implementation of dynamic beacon service with smart transmit intervals.
/// </summary>
public sealed class BeaconService : IBeaconService
{
    private DynamicBeaconMode _activeMode = DynamicBeaconMode.Walk;
    private BeaconConfiguration _walkConfig = BeaconConfiguration.CreateWalkPreset();
    private BeaconConfiguration _driveConfig = BeaconConfiguration.CreateDrivePreset();
    private BeaconConfiguration _customConfig = BeaconConfiguration.CreateCustomPreset();
    private DateTime _lastTransmitTime = DateTime.UtcNow;
    private double? _lastCourseDegrees;
    private const double EarthRadiusMeters = 6371000.0;

    public BeaconConfiguration CurrentConfiguration =>
        _activeMode switch
        {
            DynamicBeaconMode.Walk => _walkConfig,
            DynamicBeaconMode.Drive => _driveConfig,
            DynamicBeaconMode.Custom => _customConfig,
            _ => _walkConfig
        };

    public IReadOnlyList<BeaconConfiguration> AllConfigurations =>
        new[] { _walkConfig, _driveConfig, _customConfig };

    public void SetActiveMode(DynamicBeaconMode mode)
    {
        _activeMode = mode;
        _lastTransmitTime = DateTime.UtcNow;
    }

    public void UpdateCustomConfiguration(BeaconConfiguration configuration)
    {
        if (configuration.Mode != DynamicBeaconMode.Custom)
            throw new ArgumentException("Configuration must be for Custom mode.", nameof(configuration));

        _customConfig = configuration;
    }

    public BeaconTransmitDecision EvaluateLocationUpdate(LocationData currentLocation, LocationData? previousLocation)
    {
        var timeSinceLastTransmit = DateTime.UtcNow - _lastTransmitTime;
        var config = CurrentConfiguration;

        // No previous location - can't calculate speed/course, use time-based decision
        if (previousLocation == null)
        {
            var isTimeExpired = timeSinceLastTransmit.TotalSeconds >= config.SlowIntervalSeconds;
            return new BeaconTransmitDecision
            {
                ShouldTransmit = isTimeExpired,
                Reason = isTimeExpired ? "Initial beacon or time interval exceeded" : "Awaiting time interval",
                ActiveIntervalSeconds = config.SlowIntervalSeconds,
                SecondsUntilNextBeacon = Math.Max(0, config.SlowIntervalSeconds - (int)timeSinceLastTransmit.TotalSeconds)
            };
        }

        // Calculate distance between locations
        var distanceMeters = CalculateDistance(previousLocation, currentLocation);

        // Ignore if distance is below minimum threshold (GPS jitter)
        if (distanceMeters < config.MinimumDistanceMeters)
        {
            return new BeaconTransmitDecision
            {
                ShouldTransmit = false,
                Reason = $"Distance {distanceMeters:F0}m below minimum {config.MinimumDistanceMeters}m",
                ActiveIntervalSeconds = GetActiveInterval(config, 0),
                SecondsUntilNextBeacon = (int)Math.Max(1, config.SlowIntervalSeconds - timeSinceLastTransmit.TotalSeconds)
            };
        }

        // Calculate speed (time delta in seconds from location timestamps)
        var timeDelta = (currentLocation.Timestamp - previousLocation.Timestamp).TotalSeconds;
        var speedKmh = timeDelta > 0 ? (distanceMeters / timeDelta) * 3.6 : 0; // Convert m/s to km/h

        // Calculate course if possible
        var courseDegrees = CalculateCourse(previousLocation, currentLocation);
        var courseChangeThreshold = config.CourseChangeThresholdDegrees;

        // Check if course changed significantly
        if (_lastCourseDegrees.HasValue && courseChangeThreshold > 0)
        {
            var courseDelta = Math.Abs(NormalizeAngleDifference(courseDegrees - _lastCourseDegrees.Value));
            if (courseDelta >= courseChangeThreshold)
            {
                _lastCourseDegrees = courseDegrees;
                _lastTransmitTime = DateTime.UtcNow;
                var activeInterval = GetActiveInterval(config, speedKmh);

                return new BeaconTransmitDecision
                {
                    ShouldTransmit = true,
                    Reason = $"Course changed {courseDelta:F0}°",
                    CurrentSpeedKmh = speedKmh,
                    CurrentCourseDegrees = courseDegrees,
                    ActiveIntervalSeconds = activeInterval,
                    SecondsUntilNextBeacon = activeInterval
                };
            }
        }
        else
        {
            _lastCourseDegrees = courseDegrees;
        }

        // Check if time interval has expired
        var activeInterval2 = GetActiveInterval(config, speedKmh);
        var isTimeExpired2 = timeSinceLastTransmit.TotalSeconds >= activeInterval2;

        if (isTimeExpired2)
        {
            _lastTransmitTime = DateTime.UtcNow;
            return new BeaconTransmitDecision
            {
                ShouldTransmit = true,
                Reason = $"Beacon interval ({activeInterval2}s) exceeded",
                CurrentSpeedKmh = speedKmh,
                CurrentCourseDegrees = courseDegrees,
                ActiveIntervalSeconds = activeInterval2,
                SecondsUntilNextBeacon = activeInterval2
            };
        }

        // No beacon needed yet
        var secondsUntilNext = Math.Max(1, activeInterval2 - (int)timeSinceLastTransmit.TotalSeconds);
        return new BeaconTransmitDecision
        {
            ShouldTransmit = false,
            Reason = $"Waiting for next interval (in {secondsUntilNext}s)",
            CurrentSpeedKmh = speedKmh,
            CurrentCourseDegrees = courseDegrees,
            ActiveIntervalSeconds = activeInterval2,
            SecondsUntilNextBeacon = secondsUntilNext
        };
    }

    public void ResetTransmissionTimer()
    {
        _lastTransmitTime = DateTime.UtcNow;
    }

    public PositionPacket CreatePositionPacket(LocationData location, string callsign)
    {
        var config = CurrentConfiguration;
        var callsignParts = callsign.Split('-');
        var callsignBase = callsignParts[0];
        var ssid = callsignParts.Length > 1 && int.TryParse(callsignParts[1], out var ssidValue) ? ssidValue : (int?)null;

        return new PositionPacket
        {
            Source = new Callsign(callsignBase, ssid),
            Destination = new Callsign("APRS"),
            Latitude = location.Latitude,
            Longitude = location.Longitude,
            Altitude = location.Altitude.HasValue ? location.Altitude.Value * 3.28084 : null, // Convert meters to feet
            Course = _lastCourseDegrees.HasValue ? _lastCourseDegrees.Value : null,
            Symbol = new Symbol(SymbolTable.Primary, SymbolCode.LatinSmallLetterA),
            Comment = config.BeaconComment,
            Precision = 3
        };
    }

    private int GetActiveInterval(BeaconConfiguration config, double speedKmh)
    {
        if (speedKmh <= config.SlowSpeedThresholdKmh)
            return config.SlowIntervalSeconds;

        if (speedKmh <= config.FastSpeedThresholdKmh)
            return config.NormalIntervalSeconds;

        return config.FastIntervalSeconds;
    }

    /// <summary>
    /// Calculates the great-circle distance between two locations in meters using Haversine formula.
    /// </summary>
    private static double CalculateDistance(LocationData from, LocationData to)
    {
        var lat1 = ToRadians(from.Latitude);
        var lat2 = ToRadians(to.Latitude);
        var deltaLat = ToRadians(to.Latitude - from.Latitude);
        var deltaLon = ToRadians(to.Longitude - from.Longitude);

        var a = Math.Sin(deltaLat / 2) * Math.Sin(deltaLat / 2) +
                Math.Cos(lat1) * Math.Cos(lat2) *
                Math.Sin(deltaLon / 2) * Math.Sin(deltaLon / 2);

        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return EarthRadiusMeters * c;
    }

    /// <summary>
    /// Calculates the initial bearing (course) from one location to another in degrees (0-360).
    /// </summary>
    private static double CalculateCourse(LocationData from, LocationData to)
    {
        var lat1 = ToRadians(from.Latitude);
        var lat2 = ToRadians(to.Latitude);
        var deltaLon = ToRadians(to.Longitude - from.Longitude);

        var y = Math.Sin(deltaLon) * Math.Cos(lat2);
        var x = Math.Cos(lat1) * Math.Sin(lat2) -
                Math.Sin(lat1) * Math.Cos(lat2) * Math.Cos(deltaLon);

        var bearing = Math.Atan2(y, x);
        var course = (ToGradians(bearing) + 360) % 360;
        return course;
    }

    /// <summary>
    /// Normalizes an angle difference to the range [0, 180).
    /// </summary>
    private static double NormalizeAngleDifference(double angleDiff)
    {
        angleDiff = angleDiff % 360;
        if (angleDiff > 180)
            angleDiff = 360 - angleDiff;
        return Math.Abs(angleDiff);
    }

    private static double ToRadians(double degrees) => degrees * Math.PI / 180;
    private static double ToGradians(double radians) => radians * 180 / Math.PI;
}
