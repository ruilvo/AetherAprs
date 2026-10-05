// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Data;
using AetherAprs.Data.Entities;
using AetherAprs.Data.Mappers;
using AetherAprs.Models;
using Geo.Abstractions.Interfaces;
using Geo.Geodesy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AetherAprs.Services.Beaconing;

/// <summary>
/// Implementation of dynamic beacon service with timer-based state machine.
/// Evaluates beacon conditions periodically and on location updates.
/// </summary>
public sealed class BeaconService : IBeaconService, IAsyncDisposable
{
    private readonly ILogger<BeaconService> _logger;
    private readonly IDbContextFactory<AppDbContext>? _dbContextFactory;
    private readonly IGeodeticCalculator _geodeticCalculator;
    private readonly Lock _lock = new();
    private readonly Timer _evaluationTimer;
    private readonly CancellationTokenSource _disposalCts = new();

    private DynamicBeaconMode _activeMode = DynamicBeaconMode.Walk;
    private BeaconConfig _walkConfig = BeaconConfig.CreateWalkPreset();
    private BeaconConfig _driveConfig = BeaconConfig.CreateDrivePreset();
    private BeaconConfig _customConfig = BeaconConfig.CreateCustomPreset();
    private DateTime _lastTransmitTime = DateTime.UtcNow;
    private double? _lastCourseDegrees;
    private LocationData? _currentLocation;
    private LocationData? _previousLocation;
    private BeaconTransmitDecision? _currentDecision;
    private bool _disposed;

    public event EventHandler<BeaconRequestedEventArgs>? BeaconRequested;

    private const double MetersPerSecondToKilometersPerHour = 3.6;
    private const int EvaluationIntervalMilliseconds = 1000; // Check every second

    public BeaconService(ILogger<BeaconService>? logger = null)
    {
        _logger = logger ?? NullLogger<BeaconService>.Instance;
        _geodeticCalculator = new SpheroidCalculator(Spheroid.Wgs84);
        _evaluationTimer = new Timer(OnEvaluationTimer, null, EvaluationIntervalMilliseconds, EvaluationIntervalMilliseconds);
    }

    public BeaconService(
        IDbContextFactory<AppDbContext> dbContextFactory,
        ILogger<BeaconService> logger)
    {
        _dbContextFactory = dbContextFactory;
        _logger = logger ?? NullLogger<BeaconService>.Instance;
        _geodeticCalculator = new SpheroidCalculator(Spheroid.Wgs84);
        LoadFromDatabase();
        _evaluationTimer = new Timer(OnEvaluationTimer, null, EvaluationIntervalMilliseconds, EvaluationIntervalMilliseconds);
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
                return [_walkConfig, _driveConfig, _customConfig];
            }
        }
    }

    public double? LastCourseDegrees
    {
        get
        {
            lock (_lock)
            {
                return _lastCourseDegrees;
            }
        }
    }

    public BeaconTransmitDecision? CurrentDecision
    {
        get
        {
            lock (_lock)
            {
                return _currentDecision;
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

    public void ProcessLocationUpdate(LocationData currentLocation)
    {
        lock (_lock)
        {
            _previousLocation = _currentLocation;
            _currentLocation = currentLocation;
        }

        // Force immediate evaluation
        EvaluateBeaconConditions();
    }

    private void OnEvaluationTimer(object? state)
    {
        if (_disposalCts.Token.IsCancellationRequested)
            return;

        EvaluateBeaconConditions();
    }

    private void EvaluateBeaconConditions()
    {
        BeaconRequestedEventArgs? eventArgs = null;

        lock (_lock)
        {
            if (_currentLocation == null)
            {
                // No location available yet
                _currentDecision = new BeaconTransmitDecision
                {
                    ShouldTransmit = false,
                    Reason = new BeaconTransmitReason.NoLocationAvailable(),
                    ActiveIntervalSeconds = 0,
                    SecondsUntilNextBeacon = 0
                };
                return;
            }

            var decision = EvaluateLocationUpdateInternal(_currentLocation, _previousLocation);
            _currentDecision = decision;

            _logger.LogDebug(
                "Beacon evaluate: ShouldTransmit={ShouldTransmit}, Reason={Reason}, Interval={Interval}s",
                decision.ShouldTransmit,
                decision.Reason.GetLocalizedString(),
                decision.ActiveIntervalSeconds);

            if (decision.ShouldTransmit)
            {
                // Reset timer automatically when beacon is requested
                _lastTransmitTime = DateTime.UtcNow;

                // Prepare event args to raise outside the lock
                eventArgs = new BeaconRequestedEventArgs
                {
                    Location = _currentLocation,
                    Decision = decision
                };
            }
        }

        // Raise event outside the lock to prevent deadlocks
        if (eventArgs is not null)
        {
            try
            {
                BeaconRequested?.Invoke(this, eventArgs);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in BeaconRequested event handler");
            }
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
                    ? new BeaconTransmitReason.InitialOrIntervalExceeded()
                    : new BeaconTransmitReason.AwaitingTimeInterval(),
                ActiveIntervalSeconds = config.SlowIntervalSeconds,
                SecondsUntilNextBeacon = Math.Max(0, config.SlowIntervalSeconds - (int)timeSinceLastTransmit.TotalSeconds)
            };
        }

        // Use Geo package to calculate distance and bearing
        var line = _geodeticCalculator.CalculateOrthodromicLine(
            previousLocation.Location,
            currentLocation.Location);

        if (line == null)
        {
            // Points are identical or calculation failed
            return new BeaconTransmitDecision
            {
                ShouldTransmit = false,
                Reason = new BeaconTransmitReason.AwaitingTimeInterval(),
                ActiveIntervalSeconds = GetActiveInterval(config, 0),
                SecondsUntilNextBeacon = (int)Math.Max(1, config.SlowIntervalSeconds - timeSinceLastTransmit.TotalSeconds)
            };
        }

        var distanceMeters = line.Distance.SiValue;
        var courseDegrees = line.Bearing12;

        // Ignore if distance is below minimum threshold (GPS jitter)
        if (distanceMeters < config.MinimumDistanceMeters)
        {
            return new BeaconTransmitDecision
            {
                ShouldTransmit = false,
                Reason = new BeaconTransmitReason.DistanceBelowMinimum
                {
                    ActualDistanceMeters = distanceMeters,
                    MinimumDistanceMeters = config.MinimumDistanceMeters
                },
                ActiveIntervalSeconds = GetActiveInterval(config, 0),
                SecondsUntilNextBeacon = (int)Math.Max(1, config.SlowIntervalSeconds - timeSinceLastTransmit.TotalSeconds)
            };
        }

        // Calculate speed (time delta in seconds from location timestamps)
        var timeDelta = (currentLocation.Timestamp - previousLocation.Timestamp).TotalSeconds;
        var speedKmh = timeDelta > 0 ? (distanceMeters / timeDelta) * MetersPerSecondToKilometersPerHour : 0;

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
                    Reason = new BeaconTransmitReason.CourseChanged
                    {
                        CourseDeltaDegrees = courseDelta
                    },
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
                Reason = new BeaconTransmitReason.IntervalExceeded
                {
                    IntervalSeconds = activeInterval2
                },
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
            Reason = new BeaconTransmitReason.WaitingForNextInterval
            {
                SecondsUntilNext = secondsUntilNext
            },
            CurrentSpeedKmh = speedKmh,
            CurrentCourseDegrees = courseDegrees,
            ActiveIntervalSeconds = activeInterval2,
            SecondsUntilNextBeacon = secondsUntilNext
        };
    }

    private static int GetActiveInterval(BeaconConfig config, double speedKmh)
    {
        if (speedKmh <= config.SlowSpeedThresholdKmh)
            return config.SlowIntervalSeconds;

        if (speedKmh <= config.FastSpeedThresholdKmh)
            return config.NormalIntervalSeconds;

        return config.FastIntervalSeconds;
    }

    private static double NormalizeAngleDifference(double angleDegrees)
    {
        while (angleDegrees > 180)
            angleDegrees -= 360;
        while (angleDegrees < -180)
            angleDegrees += 360;
        return angleDegrees;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;
        _disposalCts.Cancel();

        await _evaluationTimer.DisposeAsync();
        _disposalCts.Dispose();
    }
}
