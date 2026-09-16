// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AetherAprs.Data;
using AetherAprs.Models;
using AetherAprs.Models.Aprs;
using AetherAprs.Localization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

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

/// <summary>
/// Implementation of dynamic beacon service with smart transmit intervals.
/// This service is thread-safe for concurrent calls to EvaluateLocationUpdate.
/// </summary>
public sealed class BeaconService : IBeaconService
{
    private readonly ILogger<BeaconService> _logger;
    private readonly IDbContextFactory<AppDbContext>? _dbContextFactory;
    private readonly object _lock = new();
    private DynamicBeaconMode _activeMode = DynamicBeaconMode.Walk;
    private BeaconConfig _walkConfig = BeaconConfig.CreateWalkPreset();
    private BeaconConfig _driveConfig = BeaconConfig.CreateDrivePreset();
    private BeaconConfig _customConfig = BeaconConfig.CreateCustomPreset();
    private DateTime _lastTransmitTime = DateTime.UtcNow;
    private double? _lastCourseDegrees;
    
    // Physical constants
    private const double EarthRadiusMeters = 6371000.0;
    private const double MetersPerSecondToKilometersPerHour = 3.6;
    private const double MetersToFeet = 3.28084;

    public BeaconService(ILogger<BeaconService>? logger = null)
    {
        _logger = logger ?? NullLogger<BeaconService>.Instance;
    }

    public BeaconService(IDbContextFactory<AppDbContext> dbContextFactory, ILogger<BeaconService> logger)
    {
        _dbContextFactory = dbContextFactory;
        _logger = logger ?? NullLogger<BeaconService>.Instance;
        LoadFromDatabase();
    }

    public BeaconConfig CurrentConfiguration
    {
        get
        {
            lock (_lock)
            {
                return _activeMode switch
                {
                    DynamicBeaconMode.Walk => _walkConfig,
                    DynamicBeaconMode.Drive => _driveConfig,
                    DynamicBeaconMode.Custom => _customConfig,
                    _ => _walkConfig
                };
            }
        }
    }

    public IReadOnlyList<BeaconConfig> AllConfigurations
    {
        get
        {
            lock (_lock)
            {
                // Return copies to prevent external modification of internal state
                return [_walkConfig, _driveConfig, _customConfig];
            }
        }
    }

    public void SetActiveMode(DynamicBeaconMode mode)
    {
        lock (_lock)
        {
            if (_activeMode == mode)
                return;

            _logger.LogInformation("Beacon mode changed from {PreviousMode} to {NewMode}", _activeMode, mode);
            _activeMode = mode;
            _lastTransmitTime = DateTime.UtcNow;
            PersistUnlocked();
        }
    }

    public void UpdateConfiguration(BeaconConfig configuration)
    {
        lock (_lock)
        {
            switch (configuration.Mode)
            {
                case DynamicBeaconMode.Walk:
                    _walkConfig = configuration;
                    break;
                case DynamicBeaconMode.Drive:
                    _driveConfig = configuration;
                    break;
                case DynamicBeaconMode.Custom:
                    _customConfig = configuration;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(configuration), configuration.Mode, "Unknown beacon mode.");
            }

            PersistUnlocked();
        }
    }

    private void LoadFromDatabase()
    {
        if (_dbContextFactory is null)
        {
            return;
        }

        using var db = _dbContextFactory.CreateDbContext();
        foreach (var record in db.BeaconConfigs.AsNoTracking())
        {
            var config = BeaconConfigMapper.ToConfig(record);
            switch (config.Mode)
            {
                case DynamicBeaconMode.Walk:
                    _walkConfig = config;
                    break;
                case DynamicBeaconMode.Drive:
                    _driveConfig = config;
                    break;
                case DynamicBeaconMode.Custom:
                    _customConfig = config;
                    break;
            }
        }

        var state = db.BeaconingState.AsNoTracking().FirstOrDefault();
        if (state is not null)
        {
            _activeMode = state.ActiveMode;
        }
    }

    private void PersistUnlocked()
    {
        if (_dbContextFactory is null)
        {
            return;
        }

        using var db = _dbContextFactory.CreateDbContext();
        UpsertConfig(db, _walkConfig);
        UpsertConfig(db, _driveConfig);
        UpsertConfig(db, _customConfig);

        var state = db.BeaconingState.Find(BeaconingStateRecord.SingletonId);
        if (state is null)
        {
            db.BeaconingState.Add(new BeaconingStateRecord { ActiveMode = _activeMode });
        }
        else
        {
            state.ActiveMode = _activeMode;
        }

        db.SaveChanges();
    }

    private static void UpsertConfig(AppDbContext db, BeaconConfig config)
    {
        var existing = db.BeaconConfigs.Find(config.Mode);
        if (existing is null)
        {
            db.BeaconConfigs.Add(BeaconConfigMapper.ToRecord(config));
        }
        else
        {
            BeaconConfigMapper.CopyTo(existing, config);
        }
    }

    public BeaconTransmitDecision EvaluateLocationUpdate(LocationData currentLocation, LocationData? previousLocation)
    {
        lock (_lock)
        {
            var decision = EvaluateLocationUpdateInternal(currentLocation, previousLocation);
            _logger.LogDebug(
                "Beacon evaluate: ShouldTransmit={ShouldTransmit}, Reason={Reason}, Interval={Interval}s",
                decision.ShouldTransmit,
                decision.Reason,
                decision.ActiveIntervalSeconds);
            return decision;
        }
    }

    private BeaconTransmitDecision EvaluateLocationUpdateInternal(LocationData currentLocation, LocationData? previousLocation)
    {
        var timeSinceLastTransmit = DateTime.UtcNow - _lastTransmitTime;
        var config = _activeMode switch
        {
            DynamicBeaconMode.Walk => _walkConfig,
            DynamicBeaconMode.Drive => _driveConfig,
            DynamicBeaconMode.Custom => _customConfig,
            _ => _walkConfig
        };

        // No previous location - can't calculate speed/course, use time-based decision
        if (previousLocation == null)
        {
            var isTimeExpired = timeSinceLastTransmit.TotalSeconds >= config.SlowIntervalSeconds;
            return new BeaconTransmitDecision
            {
                ShouldTransmit = isTimeExpired,
                Reason = isTimeExpired
                    ? Strings.Get("ReasonInitialOrIntervalExceeded")
                    : Strings.Get("ReasonAwaitingTimeInterval"),
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
                Reason = Strings.Format("ReasonDistanceBelowMinimum", distanceMeters, config.MinimumDistanceMeters),
                ActiveIntervalSeconds = GetActiveInterval(config, 0),
                SecondsUntilNextBeacon = (int)Math.Max(1, config.SlowIntervalSeconds - timeSinceLastTransmit.TotalSeconds)
            };
        }

        // Calculate speed (time delta in seconds from location timestamps)
        var timeDelta = (currentLocation.Timestamp - previousLocation.Timestamp).TotalSeconds;
        var speedKmh = timeDelta > 0 ? (distanceMeters / timeDelta) * MetersPerSecondToKilometersPerHour : 0;

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
                var activeInterval = GetActiveInterval(config, speedKmh);

                return new BeaconTransmitDecision
                {
                    ShouldTransmit = true,
                    Reason = Strings.Format("ReasonCourseChanged", courseDelta),
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
            return new BeaconTransmitDecision
            {
                ShouldTransmit = true,
                Reason = Strings.Format("ReasonIntervalExceeded", activeInterval2),
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
            Reason = Strings.Format("ReasonWaitingForNextInterval", secondsUntilNext),
            CurrentSpeedKmh = speedKmh,
            CurrentCourseDegrees = courseDegrees,
            ActiveIntervalSeconds = activeInterval2,
            SecondsUntilNextBeacon = secondsUntilNext
        };
    }

    public void ResetTransmissionTimer()
    {
        lock (_lock)
        {
            _lastTransmitTime = DateTime.UtcNow;
        }
    }

    public PositionPacket CreatePositionPacket(
        LocationData location,
        string callsign,
        string symbolTableCharacter = "/",
        string symbolCodeCharacter = "[")
    {
        BeaconConfig config;
        double? lastCourse;
        
        lock (_lock)
        {
            config = _activeMode switch
            {
                DynamicBeaconMode.Walk => _walkConfig,
                DynamicBeaconMode.Drive => _driveConfig,
                DynamicBeaconMode.Custom => _customConfig,
                _ => _walkConfig
            };
            lastCourse = _lastCourseDegrees;
        }
        
        var callsignParts = callsign.Split('-');
        var callsignBase = callsignParts[0];
        var ssid = callsignParts.Length > 1 && int.TryParse(callsignParts[1], out var ssidValue) ? ssidValue : (int?)null;

        var packet = new PositionPacket
        {
            Source = new Callsign(callsignBase, ssid),
            Destination = new Callsign("APRS"),
            Latitude = location.Latitude,
            Longitude = location.Longitude,
            Altitude = location.Altitude.HasValue ? location.Altitude.Value * MetersToFeet : null,
            Course = lastCourse,
            Symbol = CreateSymbol(symbolTableCharacter, symbolCodeCharacter),
            Comment = config.BeaconComment,
            Precision = 2
        };
        _logger.LogDebug(
            "Created position packet for {Callsign}: Lat={Latitude:F5}, Lon={Longitude:F5}",
            callsign,
            packet.Latitude,
            packet.Longitude);
        return packet;
    }

    private static Symbol CreateSymbol(string tableCharacter, string codeCharacter)
    {
        if (tableCharacter.Length != 1 || codeCharacter.Length != 1)
        {
            throw new ArgumentException("APRS symbol table and code must each contain exactly one character.");
        }

        return new Symbol(
            tableCharacter[0].ToSymbolTable(),
            codeCharacter[0].ToSymbolCode());
    }

    private static int GetActiveInterval(BeaconConfig config, double speedKmh)
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
