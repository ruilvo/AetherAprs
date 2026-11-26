// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Services.Platform;
using Android.Content;
using Android.Hardware;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading;

namespace AetherAprs.Android.Services.Platform;

/// <summary>
/// Android-specific implementation of <see cref="ICompassService"/>.
/// Fuses the accelerometer and magnetometer via <see cref="SensorManager"/>
/// to compute the device heading (degrees clockwise from north) so bearing
/// indicators can rotate with the phone.
/// </summary>
public sealed class CompassService : Java.Lang.Object, ICompassService, ISensorEventListener
{
    private readonly ILogger<CompassService>? _logger;
    private readonly SensorManager _sensorManager;
    private readonly float[] _gravity = new float[3];
    private readonly float[] _geomagnetic = new float[3];
    private readonly float[] _rotationMatrix = new float[9];
    private readonly float[] _orientation = new float[3];
    private readonly Lock _lock = new();

    private readonly Sensor? _accelerometer;
    private readonly Sensor? _magnetometer;
    private bool _hasGravity;
    private bool _hasGeomagnetic;
    private bool _disposed;

    public CompassService(ILogger<CompassService>? logger = null)
    {
        _logger = logger;
        _sensorManager = (SensorManager)global::Android.App.Application.Context
            .GetSystemService(Context.SensorService)!;

        _accelerometer = _sensorManager.GetDefaultSensor(SensorType.Accelerometer);
        _magnetometer = _sensorManager.GetDefaultSensor(SensorType.MagneticField);
    }

    /// <inheritdoc/>
    public bool IsAvailable => _accelerometer is not null && _magnetometer is not null;

    /// <inheritdoc/>
    public double? HeadingDegrees { get; private set; }

    /// <inheritdoc/>
    public event EventHandler<double>? HeadingUpdated;

    /// <inheritdoc/>
    public void StartListening()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!IsAvailable)
        {
            _logger?.LogWarning("Compass sensors are not available on this device");
            return;
        }

        _sensorManager.RegisterListener(this, _accelerometer, SensorDelay.Ui);
        _sensorManager.RegisterListener(this, _magnetometer, SensorDelay.Ui);
        _logger?.LogInformation("Compass service started");
    }

    /// <inheritdoc/>
    public void StopListening()
    {
        if (_disposed)
        {
            return;
        }

        _sensorManager.UnregisterListener(this);
        _logger?.LogInformation("Compass service stopped");
    }

    /// <inheritdoc/>
    public void OnSensorChanged(SensorEvent? e)
    {
        if (e?.Sensor is null || e.Values is null)
        {
            return;
        }

        if (e.Sensor.Type == SensorType.Accelerometer)
        {
            CopyValues(e.Values, _gravity);
            _hasGravity = true;
        }
        else if (e.Sensor.Type == SensorType.MagneticField)
        {
            CopyValues(e.Values, _geomagnetic);
            _hasGeomagnetic = true;
        }

        if (!_hasGravity || !_hasGeomagnetic)
        {
            return;
        }

        // GetRotationMatrix returns false when the device is in free-fall or the
        // magnetic field is too weak to determine orientation; keep the last heading.
        if (!SensorManager.GetRotationMatrix(_rotationMatrix, null, _gravity, _geomagnetic))
        {
            return;
        }

        SensorManager.GetOrientation(_rotationMatrix, _orientation);

        // _orientation[0] is azimuth in radians: 0 = north, positive eastward.
        // Convert to degrees clockwise from north and normalize to [0, 360).
        double azimuthDegrees = _orientation[0] * (180.0 / Math.PI);
        double heading = (azimuthDegrees + 360.0) % 360.0;

        lock (_lock)
        {
            HeadingDegrees = heading;
        }

        HeadingUpdated?.Invoke(this, heading);
    }

    /// <inheritdoc/>
    public void OnAccuracyChanged(Sensor? sensor, SensorStatus accuracy)
    {
    }

    protected override void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        StopListening();
        _disposed = true;
        base.Dispose(disposing);
    }

    private static void CopyValues(IList<float> source, float[] destination)
    {
        int count = Math.Min(source.Count, destination.Length);
        for (int i = 0; i < count; i++)
        {
            destination[i] = source[i];
        }
    }
}
