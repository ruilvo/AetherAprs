// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using AetherAprs.Models;
using AetherAprs.Services.Location;

namespace AetherAprs.Tests.Helpers;

/// <summary>
/// In-memory location coordinator for ViewModel tests: the test sets the fix and
/// can assert which purposes were acquired and released.
/// </summary>
public sealed class FakeLocationCoordinator : ILocationCoordinator, IDisposable
{
    private readonly Dictionary<LocationPurpose, int> _acquisitions = [];

    /// <summary>Gets or sets the fix the coordinator currently reports.</summary>
    public LocationData? Location { get; set; }

    /// <inheritdoc />
    public LocationSnapshot? Current => Location is null
        ? null
        : new LocationSnapshot(
            Location,
            Location.Timestamp == default ? DateTimeOffset.UtcNow : Location.Timestamp,
            Location.Accuracy > 0 ? Location.Accuracy : null);

    /// <inheritdoc />
    public IReadOnlyCollection<LocationPurpose> ActivePurposes => [.. _acquisitions.Keys];

    /// <summary>Gets whether <paramref name="purpose"/> is currently held.</summary>
    public bool IsAcquired(LocationPurpose purpose) => _acquisitions.ContainsKey(purpose);

    /// <summary>Gets how many times <paramref name="purpose"/> was acquired.</summary>
    public int AcquisitionCount(LocationPurpose purpose) =>
        _acquisitions.TryGetValue(purpose, out int count) ? count : 0;

    /// <summary>Gets how many times the worker was stopped by a release or a stop-all.</summary>
    public int StopCount { get; private set; }

    /// <summary>Gets whether the operator was asked for the location permission.</summary>
    public bool PermissionRequested { get; private set; }

    /// <inheritdoc />
    public event EventHandler<LocationSnapshot>? LocationChanged;

    /// <inheritdoc />
    public Task AcquireAsync(
        LocationPurpose purpose,
        bool requestPermission = false,
        CancellationToken cancellationToken = default)
    {
        _acquisitions[purpose] = AcquisitionCount(purpose) + 1;
        PermissionRequested |= requestPermission;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public void Release(LocationPurpose purpose)
    {
        if (_acquisitions.Remove(purpose) && _acquisitions.Count == 0)
        {
            StopCount++;
        }
    }

    /// <inheritdoc />
    public void StopAll()
    {
        _acquisitions.Clear();
        StopCount++;
    }

    /// <inheritdoc />
    public bool TryGetFresh(TimeSpan maximumAge, out LocationSnapshot? snapshot)
    {
        snapshot = Current;
        return snapshot is not null && snapshot.IsFreshAt(DateTimeOffset.UtcNow, maximumAge);
    }

    /// <summary>Publishes a new fix to the subscribers.</summary>
    public void SetLocation(LocationData location)
    {
        Location = location;
        if (Current is { } snapshot)
        {
            LocationChanged?.Invoke(this, snapshot);
        }
    }

    /// <inheritdoc />
    public void Dispose() => _acquisitions.Clear();
}
