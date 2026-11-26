// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Models;
using AetherAprs.Services.Transmission;
using AetherAprs.Tests.Services.Configuration;
using Geo;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace AetherAprs.Tests.Services.Transmission;

public sealed class BeaconTransmitQueueTests
{
    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Restart_ReturnsInterruptedSendToQueuedState()
    {
        using QueueFixture fixture = new();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        BeaconQueueItem item;
        using (FileBeaconTransmitQueue first = CreateQueue(fixture.Path))
        {
            item = first.Enqueue(Guid.NewGuid(), Location(now), now).Queued!;
            first.MarkSending(item.Id);
            await WaitForPersistedSnapshotAsync(
                first,
                fixture.Path,
                TestContext.Current.CancellationToken);
        }

        using FileBeaconTransmitQueue recovered = CreateQueue(fixture.Path);

        BeaconQueueItem pending = Assert.Single(recovered.Reconcile(now.AddMinutes(1)));
        Assert.Equal(item.Id, pending.Id);
        Assert.Equal(BeaconQueueStatus.Queued, Assert.Single(recovered.Snapshot).Status);
    }

    [Fact]
    public void Reconcile_ExpiresStaleBeaconWithTerminalStatus()
    {
        using QueueFixture fixture = new();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        using FileBeaconTransmitQueue queue = CreateQueue(fixture.Path);
        BeaconQueueItem item = queue.Enqueue(Guid.NewGuid(), Location(now), now).Queued!;

        Assert.Empty(queue.Reconcile(now.Add(FileBeaconTransmitQueue.DefaultExpiry).AddSeconds(1)));

        BeaconQueueItem expired = Assert.Single(queue.Snapshot);
        Assert.Equal(item.Id, expired.Id);
        Assert.Equal(BeaconQueueStatus.Expired, expired.Status);
    }

    [Fact]
    public async Task FailedAttempts_BecomeTerminalAfterBoundedRetryCount()
    {
        using QueueFixture fixture = new();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        using FileBeaconTransmitQueue queue = CreateQueue(fixture.Path);
        BeaconQueueItem item = queue.Enqueue(Guid.NewGuid(), Location(now), now).Queued!;

        queue.MarkAttemptFailed(item.Id, "offline", now);
        queue.MarkAttemptFailed(item.Id, "offline", now);
        queue.MarkAttemptFailed(item.Id, "offline", now);

        BeaconQueueItem failed = Assert.Single(queue.Snapshot);
        Assert.Equal(BeaconQueueStatus.Failed, failed.Status);
        Assert.Equal(FileBeaconTransmitQueue.MaximumAttempts, failed.AttemptCount);
        Assert.Empty(queue.Reconcile(now));

        await WaitForPersistedSnapshotAsync(
            queue,
            fixture.Path,
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Enqueue_WhenFull_EvictsOldestQueuedBeaconWithoutThrowing()
    {
        using QueueFixture fixture = new();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        using FileBeaconTransmitQueue queue = CreateQueue(fixture.Path, maximumItems: 1);

        BeaconEnqueueResult first = queue.Enqueue(Guid.NewGuid(), Location(now), now);
        Assert.Equal(BeaconQueueStatus.Queued, first.Status);
        Assert.NotNull(first.Queued);
        Assert.Null(first.Dropped);

        BeaconEnqueueResult second = queue.Enqueue(Guid.NewGuid(), Location(now), now);
        Assert.Equal(BeaconQueueStatus.Queued, second.Status);
        Assert.NotNull(second.Queued);
        Assert.NotNull(second.Dropped);
        Assert.Equal(first.Queued!.Id, second.Dropped!.Id);
        Assert.Equal(BeaconQueueStatus.Dropped, second.Dropped.Status);
        Assert.Equal("Queue capacity reached.", second.Dropped.Error);

        Assert.Equal(2, queue.Snapshot.Count);
        await WaitForPersistedSnapshotAsync(
            queue,
            fixture.Path,
            TestContext.Current.CancellationToken);

        List<BeaconQueueItem>? persisted = await ReadSnapshotAsync(fixture.Path, TestContext.Current.CancellationToken);
        Assert.NotNull(persisted);
        Assert.Contains(persisted!, entry =>
            entry.Id == second.Dropped!.Id && entry.Status == BeaconQueueStatus.Dropped);
        Assert.Contains(persisted!, entry =>
            entry.Id == second.Queued!.Id && entry.Status == BeaconQueueStatus.Queued);
    }

    [Fact]
    public void EmptyPortId_RepresentsBeaconWaitingForAnyUsableRoute()
    {
        using QueueFixture fixture = new();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        using FileBeaconTransmitQueue queue = CreateQueue(fixture.Path);

        BeaconQueueItem item = queue.Enqueue(Guid.Empty, Location(now), now).Queued!;

        Assert.Equal(Guid.Empty, item.PortId);
        Assert.Single(queue.Reconcile(now));
    }

    [Fact]
    public void CorruptFile_IsQuarantinedAndReportedWithWarning()
    {
        using QueueFixture fixture = new();
        const string corruptJson = "{ not valid json";
        File.WriteAllText(fixture.Path, corruptJson);
        RecordingLogger<FileBeaconTransmitQueue> logger = new();

        using FileBeaconTransmitQueue queue = new(fixture.Path, logger);

        Assert.Empty(queue.Snapshot);
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Warning);

        string quarantinePath = Assert.Single(
            Directory.GetFiles(fixture.Directory, "beacons.json.corrupt-*"));
        Assert.Equal(corruptJson, File.ReadAllText(quarantinePath));
        Assert.False(File.Exists(fixture.Path));
    }

    [Fact]
    public async Task RapidTransitions_CoalesceIntoFewWritesAndPersistLatestSnapshot()
    {
        using QueueFixture fixture = new();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        using FileBeaconTransmitQueue queue = CreateQueue(fixture.Path);
        BeaconQueueItem item = queue.Enqueue(Guid.NewGuid(), Location(now), now).Queued!;

        for (int attempt = 0; attempt < 50; attempt++)
        {
            queue.MarkSending(item.Id);
            queue.MarkAttemptFailed(item.Id, "offline", now);
        }

        queue.MarkSent(item.Id);
        await WaitForPersistedSnapshotAsync(
            queue,
            fixture.Path,
            TestContext.Current.CancellationToken);

        BeaconQueueItem written = Assert.Single(queue.Snapshot);
        Assert.Equal(item.Id, written.Id);
        Assert.Equal(BeaconQueueStatus.Sent, written.Status);
        Assert.Equal(50, written.AttemptCount);
        Assert.True(
            queue.PersistWriteCount <= 10,
            $"Rapid transitions should coalesce; observed {queue.PersistWriteCount} writes.");
    }

    [Fact]
    public async Task DisposeWhilePersistInProgress_CompletesWithoutDeadlock()
    {
        using QueueFixture fixture = new();
        FileBeaconTransmitQueue queue = CreateQueue(fixture.Path);
        TaskCompletionSource writeStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource releaseWrite = new(TaskCreationOptions.RunContinuationsAsynchronously);
        queue.PersistBarrierForTests = async cancellationToken =>
        {
            writeStarted.TrySetResult();
            await releaseWrite.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        };

        try
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            queue.Enqueue(Guid.NewGuid(), Location(now), now);
            await writeStarted.Task.WaitAsync(
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);

            Task disposeTask = Task.Run(queue.Dispose, TestContext.Current.CancellationToken);
            releaseWrite.TrySetResult();
            await disposeTask.WaitAsync(
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);
        }
        finally
        {
            releaseWrite.TrySetResult();
            queue.Dispose();
        }
    }

    [Fact]
    public async Task DisposeWithStuckPersist_StopsWithinBoundedTimeout()
    {
        using QueueFixture fixture = new();
        FileBeaconTransmitQueue queue = CreateQueue(fixture.Path);
        queue.PersistShutdownTimeout = TimeSpan.FromMilliseconds(250);
        TaskCompletionSource writeStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        queue.PersistBarrierForTests = async cancellationToken =>
        {
            writeStarted.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
        };

        try
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            queue.Enqueue(Guid.NewGuid(), Location(now), now);
            await writeStarted.Task.WaitAsync(
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);

            Stopwatch stopwatch = System.Diagnostics.Stopwatch.StartNew();
            await Task.Run(queue.Dispose, TestContext.Current.CancellationToken).WaitAsync(
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);
            stopwatch.Stop();

            Assert.True(
                stopwatch.Elapsed < TimeSpan.FromSeconds(4),
                $"Dispose took {stopwatch.Elapsed} despite a stuck persist.");
        }
        finally
        {
            queue.Dispose();
        }
    }

    private static FileBeaconTransmitQueue CreateQueue(
        string path,
        int maximumItems = FileBeaconTransmitQueue.DefaultMaximumItems) =>
        new(path,
            NullLogger<FileBeaconTransmitQueue>.Instance,
            maximumItems);

    private static LocationData Location(DateTimeOffset timestamp) =>
        new()
        {
            Location = new Coordinate(38.5, -9.1),
            Altitude = 12,
            Timestamp = timestamp
        };

    /// <summary>
    /// Waits until the on-disk snapshot matches the in-memory snapshot, proving the
    /// coalescing writer flushed the latest state.
    /// </summary>
    private static async Task WaitForPersistedSnapshotAsync(
        FileBeaconTransmitQueue queue,
        string path,
        CancellationToken cancellationToken)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(5);
        List<BeaconQueueItem> expected = [.. queue.Snapshot];
        string lastJson = "<no file>";
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            expected = [.. queue.Snapshot];
            string? rawJson = await ReadRawAsync(path, cancellationToken).ConfigureAwait(false);
            if (rawJson is not null)
            {
                lastJson = rawJson;
            }

            List<BeaconQueueItem>? actual = Deserialize(rawJson);
            if (actual is not null && actual.SequenceEqual(expected))
            {
                return;
            }

            await Task.Delay(10, cancellationToken).ConfigureAwait(false);
        }

        throw new TimeoutException(
            $"Beacon outbox at {path} did not match the in-memory snapshot before the timeout. "
            + $"Expected: {JsonSerializer.Serialize(expected, _jsonOptions)}. Actual: {lastJson}. "
            + $"Writes: {queue.PersistWriteCount}.");
    }

    private static async Task<string?> ReadRawAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            // Share delete so the atomic temp-file move can replace the file while
            // the test is reading it (Windows otherwise reports a sharing violation).
            await using FileStream stream = new(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 4096,
                useAsync: true);
            using StreamReader reader = new(stream);
            return await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static List<BeaconQueueItem>? Deserialize(string? json)
    {
        if (json is null)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<List<BeaconQueueItem>>(json, _jsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static async Task<List<BeaconQueueItem>?> ReadSnapshotAsync(
        string path,
        CancellationToken cancellationToken) =>
        Deserialize(await ReadRawAsync(path, cancellationToken).ConfigureAwait(false));

    private sealed class QueueFixture : IDisposable
    {
        private readonly string _directory = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"AetherAprsBeaconQueue-{Guid.NewGuid():N}");

        public QueueFixture()
        {
            System.IO.Directory.CreateDirectory(_directory);
            Path = System.IO.Path.Combine(_directory, "beacons.json");
        }

        public string Path { get; }

        public string Directory => _directory;

        public void Dispose()
        {
            if (System.IO.Directory.Exists(_directory))
            {
                System.IO.Directory.Delete(_directory, recursive: true);
            }
        }
    }
}
