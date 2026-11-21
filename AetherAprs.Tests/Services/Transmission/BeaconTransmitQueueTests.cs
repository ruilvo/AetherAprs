// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Models;
using AetherAprs.Services.Transmission;
using Geo;
using System;
using System.IO;
using Xunit;

namespace AetherAprs.Tests.Services.Transmission;

public sealed class BeaconTransmitQueueTests
{
    [Fact]
    public void Restart_ReturnsInterruptedSendToQueuedState()
    {
        using var fixture = new QueueFixture();
        var now = DateTimeOffset.UtcNow;
        var first = new FileBeaconTransmitQueue(fixture.Path);
        var item = first.Enqueue(Guid.NewGuid(), Location(now), now);
        first.MarkSending(item.Id);

        var recovered = new FileBeaconTransmitQueue(fixture.Path);

        var pending = Assert.Single(recovered.Reconcile(now.AddMinutes(1)));
        Assert.Equal(item.Id, pending.Id);
        Assert.Equal(BeaconQueueStatus.Queued, Assert.Single(recovered.Snapshot).Status);
    }

    [Fact]
    public void Reconcile_ExpiresStaleBeaconWithTerminalStatus()
    {
        using var fixture = new QueueFixture();
        var now = DateTimeOffset.UtcNow;
        var queue = new FileBeaconTransmitQueue(fixture.Path);
        var item = queue.Enqueue(Guid.NewGuid(), Location(now), now);

        Assert.Empty(queue.Reconcile(now.Add(FileBeaconTransmitQueue.DefaultExpiry).AddSeconds(1)));

        var expired = Assert.Single(queue.Snapshot);
        Assert.Equal(item.Id, expired.Id);
        Assert.Equal(BeaconQueueStatus.Expired, expired.Status);
    }

    [Fact]
    public void FailedAttempts_BecomeTerminalAfterBoundedRetryCount()
    {
        using var fixture = new QueueFixture();
        var now = DateTimeOffset.UtcNow;
        var queue = new FileBeaconTransmitQueue(fixture.Path);
        var item = queue.Enqueue(Guid.NewGuid(), Location(now), now);

        queue.MarkAttemptFailed(item.Id, "offline", now);
        queue.MarkAttemptFailed(item.Id, "offline", now);
        queue.MarkAttemptFailed(item.Id, "offline", now);

        var failed = Assert.Single(queue.Snapshot);
        Assert.Equal(BeaconQueueStatus.Failed, failed.Status);
        Assert.Equal(FileBeaconTransmitQueue.MaximumAttempts, failed.AttemptCount);
        Assert.Empty(queue.Reconcile(now));
    }

    [Fact]
    public void Enqueue_IsBoundedAndRecordsDroppedTerminalItem()
    {
        using var fixture = new QueueFixture();
        var queue = new FileBeaconTransmitQueue(fixture.Path, maximumItems: 1);
        var now = DateTimeOffset.UtcNow;
        queue.Enqueue(Guid.NewGuid(), Location(now), now);

        Assert.Throws<InvalidOperationException>(
            () => queue.Enqueue(Guid.NewGuid(), Location(now), now));
        Assert.Equal(BeaconQueueStatus.Dropped, Assert.Single(queue.Snapshot).Status);
    }

    [Fact]
    public void EmptyPortId_RepresentsBeaconWaitingForAnyUsableRoute()
    {
        using var fixture = new QueueFixture();
        var now = DateTimeOffset.UtcNow;
        var queue = new FileBeaconTransmitQueue(fixture.Path);

        var item = queue.Enqueue(Guid.Empty, Location(now), now);

        Assert.Equal(Guid.Empty, item.PortId);
        Assert.Single(queue.Reconcile(now));
    }

    private static LocationData Location(DateTimeOffset timestamp) =>
        new()
        {
            Location = new Coordinate(38.5, -9.1),
            Altitude = 12,
            Timestamp = timestamp
        };

    private sealed class QueueFixture : IDisposable
    {
        private readonly string _directory = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"AetherAprsBeaconQueue-{Guid.NewGuid():N}");

        public QueueFixture()
        {
            Directory.CreateDirectory(_directory);
            Path = System.IO.Path.Combine(_directory, "beacons.json");
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
    }
}
