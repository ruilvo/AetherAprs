// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace AetherAprs.Services.Transmission;

public enum BeaconQueueStatus
{
    Queued,
    Sending,
    Sent,
    Expired,
    Failed,
    Dropped
}

public sealed record BeaconQueueItem(
    Guid Id,
    Guid PortId,
    double Latitude,
    double Longitude,
    double? Altitude,
    DateTimeOffset CreatedUtc,
    DateTimeOffset ExpiresUtc,
    int AttemptCount = 0,
    BeaconQueueStatus Status = BeaconQueueStatus.Queued,
    string? Error = null);

public interface IBeaconTransmitQueue
{
    BeaconQueueItem Enqueue(Guid portId, LocationData location, DateTimeOffset now);

    IReadOnlyList<BeaconQueueItem> Reconcile(DateTimeOffset now);

    void MarkSending(Guid id);

    void MarkSent(Guid id);

    void MarkAttemptFailed(Guid id, string failureReason, DateTimeOffset now);

    IReadOnlyList<BeaconQueueItem> Snapshot { get; }
}

/// <summary>
/// Bounded, atomic file-backed beacon outbox. Interrupted sends are returned to
/// the queued state when the process constructs the store again.
/// </summary>
public sealed class FileBeaconTransmitQueue : IBeaconTransmitQueue
{
    public const int DefaultMaximumItems = 100;
    public const int MaximumAttempts = 3;
    public static readonly TimeSpan DefaultExpiry = TimeSpan.FromMinutes(15);

    private readonly string _path;
    private readonly int _maximumItems;
    private readonly object _gate = new();
    private readonly List<BeaconQueueItem> _items;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    public FileBeaconTransmitQueue(
        string path,
        int maximumItems = DefaultMaximumItems)
    {
        if (maximumItems <= 0)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumItems);
        }

        _path = path;
        _maximumItems = maximumItems;
        _items = Load(path);
        ReconcileLocked(DateTimeOffset.UtcNow);
    }

    public IReadOnlyList<BeaconQueueItem> Snapshot
    {
        get
        {
            lock (_gate)
            {
                return [.. _items];
            }
        }
    }

    public BeaconQueueItem Enqueue(Guid portId, LocationData location, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(location);
        lock (_gate)
        {
            ReconcileLocked(now);
            var activeCount = _items.Count(item =>
                item.Status is BeaconQueueStatus.Queued or BeaconQueueStatus.Sending);
            if (activeCount >= _maximumItems)
            {
                var droppedIndex = _items.FindIndex(item => item.Status == BeaconQueueStatus.Queued);
                if (droppedIndex >= 0)
                {
                    _items[droppedIndex] = _items[droppedIndex] with
                    {
                        Status = BeaconQueueStatus.Dropped,
                        Error = "Queue capacity reached."
                    };
                    PersistLocked();
                }

                throw new InvalidOperationException("Beacon transmit queue is full.");
            }

            var item = new BeaconQueueItem(
                Guid.NewGuid(),
                portId,
                location.Location.Latitude,
                location.Location.Longitude,
                location.Altitude,
                now,
                now.Add(DefaultExpiry));
            _items.Add(item);
            PersistLocked();
            return item;
        }
    }

    public IReadOnlyList<BeaconQueueItem> Reconcile(DateTimeOffset now)
    {
        lock (_gate)
        {
            ReconcileLocked(now);
            return [.. _items.Where(item => item.Status == BeaconQueueStatus.Queued)];
        }
    }

    public void MarkSending(Guid id)
    {
        Update(id, item => item with { Status = BeaconQueueStatus.Sending });
    }

    public void MarkSent(Guid id)
    {
        Update(id, item => item with { Status = BeaconQueueStatus.Sent });
    }

    public void MarkAttemptFailed(Guid id, string failureReason, DateTimeOffset now)
    {
        lock (_gate)
        {
            var index = _items.FindIndex(item => item.Id == id);
            if (index < 0)
            {
                return;
            }

            var item = _items[index];
            var attempts = item.AttemptCount + 1;
            _items[index] = item with
            {
                AttemptCount = attempts,
                Status = attempts >= MaximumAttempts || now >= item.ExpiresUtc
                    ? BeaconQueueStatus.Failed
                    : BeaconQueueStatus.Queued,
                Error = failureReason
            };
            PersistLocked();
        }
    }

    private void ReconcileLocked(DateTimeOffset now)
    {
        var changed = false;
        for (var index = 0; index < _items.Count; index++)
        {
            var item = _items[index];
            if (item.Status == BeaconQueueStatus.Sending)
            {
                _items[index] = item with { Status = BeaconQueueStatus.Queued };
                changed = true;
                item = _items[index];
            }

            if (item.Status is BeaconQueueStatus.Queued or BeaconQueueStatus.Sending
                && now >= item.ExpiresUtc)
            {
                _items[index] = item with
                {
                    Status = BeaconQueueStatus.Expired,
                    Error = "Beacon expired before transmission."
                };
                changed = true;
            }
        }

        if (changed)
        {
            PersistLocked();
        }
    }

    private void Update(Guid id, Func<BeaconQueueItem, BeaconQueueItem> update)
    {
        lock (_gate)
        {
            var index = _items.FindIndex(item => item.Id == id);
            if (index >= 0)
            {
                _items[index] = update(_items[index]);
                PersistLocked();
            }
        }
    }

    private void PersistLocked()
    {
        var directory = Path.GetDirectoryName(_path);
        if (directory is not null)
        {
            Directory.CreateDirectory(directory);
        }

        var temporaryPath = $"{_path}.{Guid.NewGuid():N}.tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(_items, _jsonOptions));
        File.Move(temporaryPath, _path, overwrite: true);
    }

    private List<BeaconQueueItem> Load(string path)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<BeaconQueueItem>>(
                File.ReadAllText(path),
                _jsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
