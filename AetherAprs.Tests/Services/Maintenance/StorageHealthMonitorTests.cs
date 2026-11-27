// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AetherAprs.Services.Diagnostics;
using AetherAprs.Services.Maintenance;
using AetherAprs.Services.Packets;
using AetherAprs.Services.Platform;
using AetherAprs.Services.UI;
using AetherAprs.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
using AprsAddress = AetherAprs.Models.Aprs.AprsAddress;
using AprsPacket = AetherAprs.Models.Aprs.Packets.AprsPacket;
using MessageDeliveryStatus = AetherAprs.Models.Messaging.MessageDeliveryStatus;
using MessagePacket = AetherAprs.Models.Aprs.Packets.MessagePacket;
using Xunit;

namespace AetherAprs.Tests.Services.Maintenance;

public sealed class StorageHealthMonitorTests
{
    private const long WarningBound = 200L * 1024 * 1024;
    private const long CriticalBound = 50L * 1024 * 1024;

    [Theory]
    [InlineData(0)]
    [InlineData(CriticalBound - 1)]
    public void FreeSpaceAtOrBelowTheCriticalBoundIsCritical(long available)
    {
        using StorageHealthMonitor monitor = CreateMonitor(available);

        Assert.Equal(StorageHealthLevel.Critical, monitor.Classify(available));
    }

    [Fact]
    public void FreeSpaceBetweenTheBoundsIsAWarning()
    {
        using StorageHealthMonitor monitor = CreateMonitor(CriticalBound + 1);

        Assert.Equal(StorageHealthLevel.Warning, monitor.Classify(CriticalBound + 1));
        Assert.Equal(StorageHealthLevel.Warning, monitor.Classify(WarningBound));
    }

    [Fact]
    public void FreeSpaceAboveTheWarningBoundIsHealthy()
    {
        using StorageHealthMonitor monitor = CreateMonitor(WarningBound + 1);

        Assert.Equal(StorageHealthLevel.Healthy, monitor.Classify(WarningBound + 1));
    }

    [Fact]
    public async Task RefreshReportsUnknownWhenThePlatformCannotMeasureFreeSpace()
    {
        using StorageHealthMonitor monitor = CreateMonitor(long.MaxValue);

        StorageHealthSnapshot snapshot = await monitor.RefreshAsync(
            TestContext.Current.CancellationToken);

        // An unmeasurable volume must never be reported as healthy.
        Assert.Equal(StorageHealthLevel.Unknown, snapshot.Level);
        Assert.Null(snapshot.AvailableBytes);
        Assert.False(snapshot.NeedsAttention);
    }

    [Fact]
    public async Task RefreshSamplesThePrivateDirectoryAndStampsTheClock()
    {
        using TestClock clock = new();
        using StorageHealthMonitor monitor = CreateMonitor(
            WarningBound * 2,
            timeProvider: clock,
            warpDirectoryPath: Environment.CurrentDirectory);

        StorageHealthSnapshot snapshot = await monitor.RefreshAsync(
            TestContext.Current.CancellationToken);

        Assert.Equal(StorageHealthLevel.Healthy, snapshot.Level);
        Assert.Equal(WarningBound * 2, snapshot.AvailableBytes);
        Assert.Equal(clock.UtcNow, snapshot.CheckedAtUtc);
        Assert.Equal(WarningBound, snapshot.WarningThresholdBytes);
        Assert.Equal(CriticalBound, snapshot.CriticalThresholdBytes);
    }

    [Fact]
    public async Task RefreshPublishesOncePerChangeAndNotifiesObservers()
    {
        using StorageHealthMonitor monitor = CreateMonitor(WarningBound * 2);
        List<StorageHealthSnapshot> observed = [];
        monitor.HealthChanged += (_, snapshot) => observed.Add(snapshot);

        await monitor.RefreshAsync(TestContext.Current.CancellationToken);
        await monitor.RefreshAsync(TestContext.Current.CancellationToken);

        // The second sample is identical, so observers are not woken again.
        Assert.Single(observed);
        Assert.Equal(StorageHealthLevel.Healthy, observed[0].Level);
    }

    [Fact]
    public async Task PressureFromPacketStorageIsReportedAsCriticalImmediately()
    {
        using StorageHealthMonitor monitor = CreateMonitor(WarningBound * 2);
        RecordingPacketStorage storage = new();
        monitor.Observe(storage);

        await monitor.RefreshAsync(TestContext.Current.CancellationToken);
        Assert.Equal(StorageHealthLevel.Healthy, monitor.Current.Level);

        storage.RaisePressure();

        Assert.Equal(StorageHealthLevel.Critical, monitor.Current.Level);
        Assert.True(monitor.Current.NeedsAttention);
    }

    [Fact]
    public void ObservingAnotherStorageDetachesThePreviousOne()
    {
        using StorageHealthMonitor monitor = CreateMonitor(WarningBound * 2);
        RecordingPacketStorage first = new();
        RecordingPacketStorage second = new();

        monitor.Observe(first);
        monitor.Observe(second);

        Assert.Equal(0, first.SubscriberCount);
        Assert.Equal(1, second.SubscriberCount);
    }

    [Fact]
    public void DisposeDetachesFromTheObservedStorage()
    {
        StorageHealthMonitor monitor = CreateMonitor(WarningBound * 2);
        RecordingPacketStorage storage = new();
        monitor.Observe(storage);

        monitor.Dispose();

        Assert.Equal(0, storage.SubscriberCount);
    }

    [Fact]
    public async Task RunLoopSamplesOnTheIntervalUntilDisposed()
    {
        using TestClock clock = new();
        using StorageHealthMonitor monitor = CreateMonitor(
            WarningBound * 2,
            timeProvider: clock,
            interval: TimeSpan.FromMinutes(15));

        await monitor.StartAsync(TestContext.Current.CancellationToken);
        await clock.WaitForTimerAsync(TestContext.Current.CancellationToken);

        Assert.Equal(StorageHealthLevel.Healthy, monitor.Current.Level);
    }

    [Fact]
    public async Task StartIsIdempotent()
    {
        using TestClock clock = new();
        using StorageHealthMonitor monitor = CreateMonitor(
            WarningBound * 2,
            timeProvider: clock);

        await monitor.StartAsync(TestContext.Current.CancellationToken);
        await monitor.StartAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, clock.PendingTimerCount);
    }

    [Fact]
    public void HealthThresholdsAreOrdered()
    {
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            CreateMonitor(CriticalBound, criticalThresholdBytes: WarningBound));

        Assert.Equal("warningThresholdBytes", exception.ParamName);
    }

    [Theory]
    [InlineData(0, "0 KB")]
    [InlineData(2048, "2 KB")]
    [InlineData(5L * 1024 * 1024, "5 MB")]
    [InlineData(3L * 1024 * 1024 * 1024, "3 GB")]
    public void SizesAreFormattedWithBinaryUnits(long bytes, string expected)
    {
        Assert.Equal(expected, StorageSizeFormatter.Format(bytes));
    }

    private static StorageHealthMonitor CreateMonitor(
        long availableBytes,
        TestClock? timeProvider = null,
        long warningThresholdBytes = WarningBound,
        long criticalThresholdBytes = CriticalBound,
        TimeSpan? interval = null,
        string? warpDirectoryPath = null) => new(
            new TestAppDataDirProvider(warpDirectoryPath ?? Environment.CurrentDirectory),
            new FixedFreeSpaceProvider(availableBytes),
            NullLogger<StorageHealthMonitor>.Instance,
            new BoundedDiagnosticEventStore(),
            timeProvider,
            warningThresholdBytes,
            criticalThresholdBytes,
            interval);

    private sealed class FixedFreeSpaceProvider(long availableBytes) : IFreeSpaceProvider
    {
        public long GetAvailableBytes(string directoryPath) => availableBytes;
    }

    private sealed class TestAppDataDirProvider(string directory) : IAppDataDirProviderService
    {
        public string GetAppDataDirectory() => directory;

        public string GetPrivateAppDataDirectory() => directory;
    }

    private sealed class RecordingPacketStorage : IPacketStorageService
    {
        private EventHandler<StoragePressureEventArgs>? _pressure;

        public int SubscriberCount => _pressure?.GetInvocationList().Length ?? 0;

        public event EventHandler? PacketStored
        {
            add { }
            remove { }
        }

        public event EventHandler<StoragePressureEventArgs>? StoragePressureDetected
        {
            add => _pressure += value;
            remove => _pressure -= value;
        }

        public void RaisePressure() => _pressure?.Invoke(
            this,
            new StoragePressureEventArgs("packet.insert", new InvalidOperationException("disk full")));

        public Task StorePacketAsync(
            AprsPacket packet, Guid? portId, bool isOutbound = false,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task EnsureOutboundMessageAsync(
            MessagePacket packet, Guid? portId, int retryCount = 0,
            DateTimeOffset? nextRetryTime = null, Guid? logicalId = null,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task UpdateOutboundMessageStatusAsync(
            AprsAddress addressee, int messageNumber,
            MessageDeliveryStatus deliveryStatus, int retryCount,
            DateTimeOffset? nextRetryTime, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task CleanupOldPacketsAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
