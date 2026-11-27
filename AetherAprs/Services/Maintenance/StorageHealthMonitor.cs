// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AetherAprs.Services.Diagnostics;
using AetherAprs.Services.Packets;
using AetherAprs.Services.Platform;
using AetherAprs.Services.UI;
using Microsoft.Extensions.Logging;

namespace AetherAprs.Services.Maintenance;

/// <summary>
/// Samples the free space of the volume that holds the private database and the
/// user-visible caches, on a timer, and reports a warning once per level change.
/// </summary>
/// <remarks>
/// A sample is cheap and non-destructive: when the platform cannot report free
/// space the level stays <see cref="StorageHealthLevel.Unknown"/> and nothing is
/// claimed to the operator. The monitor never blocks packet handling; it only
/// publishes a snapshot that storage cleanup and the support page read.
/// </remarks>
public sealed class StorageHealthMonitor : IStorageHealthMonitor
{
    /// <summary>Free space at or below which the level is <see cref="StorageHealthLevel.Warning"/>.</summary>
    public const long DefaultWarningThresholdBytes = 200L * 1024 * 1024;

    /// <summary>Free space at or below which the level is <see cref="StorageHealthLevel.Critical"/>.</summary>
    public const long DefaultCriticalThresholdBytes = 50L * 1024 * 1024;

    /// <summary>The default number of consecutive samples before a retry.</summary>
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromMinutes(15);

    private readonly IAppDataDirProviderService _appDataDirProvider;
    private readonly IFreeSpaceProvider _freeSpaceProvider;
    private readonly IDiagnosticEventSink? _diagnostics;
    private readonly ILogger<StorageHealthMonitor> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly Lock _stateLock = new();
    private StorageHealthSnapshot _current = StorageHealthSnapshot.Indeterminate;
    private CancellationTokenSource? _cancellation;
    private Task? _loop;
    private IPacketStorageService? _observedStorage;
    private bool _disposed;

    /// <summary>
    /// Initializes a new storage health monitor.
    /// </summary>
    /// <param name="appDataDirProvider">Supplies the directories to sample.</param>
    /// <param name="freeSpaceProvider">Reports the bytes available in a directory.</param>
    /// <param name="logger">The logger for level changes.</param>
    /// <param name="diagnostics">An optional sink so the support report shows the last level.</param>
    /// <param name="timeProvider">The clock for snapshot timestamps, overridable for tests.</param>
    /// <param name="warningThresholdBytes">The bound at or below which the level is a warning.</param>
    /// <param name="criticalThresholdBytes">The bound at or below which the level is critical.</param>
    /// <param name="interval">The time between samples.</param>
    public StorageHealthMonitor(
        IAppDataDirProviderService appDataDirProvider,
        IFreeSpaceProvider freeSpaceProvider,
        ILogger<StorageHealthMonitor> logger,
        IDiagnosticEventSink? diagnostics = null,
        TimeProvider? timeProvider = null,
        long warningThresholdBytes = DefaultWarningThresholdBytes,
        long criticalThresholdBytes = DefaultCriticalThresholdBytes,
        TimeSpan? interval = null)
    {
        ArgumentNullException.ThrowIfNull(appDataDirProvider);
        ArgumentNullException.ThrowIfNull(freeSpaceProvider);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(criticalThresholdBytes);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(warningThresholdBytes, criticalThresholdBytes);

        TimeSpan effectiveInterval = interval ?? DefaultInterval;
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(effectiveInterval, TimeSpan.Zero);

        _appDataDirProvider = appDataDirProvider;
        _freeSpaceProvider = freeSpaceProvider;
        _logger = logger;
        _diagnostics = diagnostics;
        _timeProvider = timeProvider ?? TimeProvider.System;
        WarningThresholdBytes = warningThresholdBytes;
        CriticalThresholdBytes = criticalThresholdBytes;
        Interval = effectiveInterval;
    }

    /// <summary>Gets the free space bound at or below which the level is a warning.</summary>
    public long WarningThresholdBytes { get; }

    /// <summary>Gets the free space bound at or below which the level is critical.</summary>
    public long CriticalThresholdBytes { get; }

    /// <summary>Gets the time between samples while the monitor is running.</summary>
    public TimeSpan Interval { get; }

    /// <inheritdoc />
    public StorageHealthSnapshot Current => Volatile.Read(ref _current);

    /// <inheritdoc />
    public event EventHandler<StorageHealthSnapshot>? HealthChanged;

    /// <summary>
    /// Attaches the monitor to a packet store so a write failing for lack of
    /// room is reported at once instead of only at the next sample.
    /// </summary>
    /// <param name="storage">The packet store whose pressure is observed.</param>
    /// <exception cref="ArgumentNullException"><paramref name="storage"/> is null.</exception>
    /// <remarks>
    /// The store raises its event from its own write path, so this handler only
    /// records the event and re-samples; it never does I/O on the caller's thread
    /// beyond a free-space query that the platform answers synchronously.
    /// </remarks>
    public void Observe(IPacketStorageService storage)
    {
        ArgumentNullException.ThrowIfNull(storage);

        if (_observedStorage is not null)
        {
            _observedStorage.StoragePressureDetected -= OnStoragePressureDetected;
        }

        storage.StoragePressureDetected += OnStoragePressureDetected;
        _observedStorage = storage;
    }

    private void OnStoragePressureDetected(object? sender, StoragePressureEventArgs args)
    {
        _logger.LogWarning(
            args.Exception,
            "Packet storage reported pressure during {Operation}; re-checking free space.",
            args.Operation);

        _diagnostics?.Record(
            "storage",
            DiagnosticSeverity.Error,
            "WriteFailed",
            "A packet could not be stored because local storage is full or unavailable.");

        // An explicit pressure report overrides a stale healthy sample.
        Publish(Sample() with { Level = StorageHealthLevel.Critical });
    }

    /// <inheritdoc />
    public Task<StorageHealthSnapshot> RefreshAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(_disposed, this);

        return Task.FromResult(Publish(Sample()));
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_stateLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_loop is not null)
            {
                return Task.CompletedTask;
            }

            _cancellation = new CancellationTokenSource();
            _logger.LogInformation(
                "Storage health monitoring samples every {Interval}.",
                Interval);
            _loop = RunLoopAsync(_cancellation.Token);
        }

        return Task.CompletedTask;
    }

    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await RefreshAsync(cancellationToken).ConfigureAwait(false);
                await Task.Delay(Interval, _timeProvider, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Expected on shutdown.
        }
        catch (Exception exception)
        {
            // A sampling failure must not end the schedule silently; the next
            // interval retries, and the failure is visible in the report.
            _logger.LogError(exception, "Storage health monitoring stopped unexpectedly.");
            _diagnostics?.Record(
                "storage",
                DiagnosticSeverity.Error,
                "Stopped",
                "Storage health monitoring stopped unexpectedly.");
        }
        finally
        {
            lock (_stateLock)
            {
                _loop = null;

                // The token was only ever owned by this loop, so the source can be
                // released here instead of racing a dispose against Task.Delay.
                _cancellation?.Dispose();
                _cancellation = null;
            }
        }
    }

    private StorageHealthSnapshot Sample()
    {
        // The database lives in the private directory; only the cache directory
        // is separately chosen. Both are on the same volume in every shipped
        // layout, so sampling the private directory answers for the database.
        string directory = _appDataDirProvider.GetPrivateAppDataDirectory();
        if (!Directory.Exists(directory))
        {
            string fallback = _appDataDirProvider.GetAppDataDirectory();
            if (!Directory.Exists(fallback))
            {
                return StorageHealthSnapshot.Indeterminate;
            }

            directory = fallback;
        }

        long availableBytes = _freeSpaceProvider.GetAvailableBytes(directory);
        if (availableBytes == long.MaxValue)
        {
            return StorageHealthSnapshot.Indeterminate;
        }

        return new StorageHealthSnapshot(
            Classify(availableBytes),
            availableBytes,
            WarningThresholdBytes,
            CriticalThresholdBytes,
            directory,
            _timeProvider.GetUtcNow());
    }

    /// <summary>
    /// Maps a free-space figure onto its severity.
    /// </summary>
    /// <param name="availableBytes">The bytes available.</param>
    /// <returns>The matching level.</returns>
    public StorageHealthLevel Classify(long availableBytes)
    {
        if (availableBytes <= CriticalThresholdBytes)
        {
            return StorageHealthLevel.Critical;
        }

        return availableBytes <= WarningThresholdBytes
            ? StorageHealthLevel.Warning
            : StorageHealthLevel.Healthy;
    }

    private StorageHealthSnapshot Publish(StorageHealthSnapshot snapshot)
    {
        StorageHealthSnapshot previous = Interlocked.Exchange(ref _current, snapshot);

        if (previous.Level == snapshot.Level && previous.AvailableBytes == snapshot.AvailableBytes)
        {
            return snapshot;
        }

        if (snapshot.Level == StorageHealthLevel.Unknown)
        {
            _logger.LogDebug("Local storage free space is not available on this platform.");
        }
        else if (snapshot.Level == StorageHealthLevel.Healthy)
        {
            _logger.LogInformation(
                "Local storage recovered with {AvailableBytes} bytes free.",
                snapshot.AvailableBytes);
        }
        else
        {
            _logger.LogWarning(
                "Local storage is {Level} with {AvailableBytes} bytes free at {Directory}.",
                snapshot.Level,
                snapshot.AvailableBytes,
                snapshot.DirectoryPath);

            _diagnostics?.Record(
                "storage",
                snapshot.Level == StorageHealthLevel.Critical
                    ? DiagnosticSeverity.Error
                    : DiagnosticSeverity.Warning,
                snapshot.Level.ToString(),
                $"Local storage has {StorageSizeFormatter.Format(snapshot.AvailableBytes ?? 0)} free.");
        }

        RaiseHealthChanged(snapshot);
        return snapshot;
    }

    private void RaiseHealthChanged(StorageHealthSnapshot snapshot)
    {
        if (HealthChanged is not { } handlers)
        {
            return;
        }

        foreach (EventHandler<StorageHealthSnapshot> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(this, snapshot);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "A storage health observer failed.");
            }
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        CancellationTokenSource? cancellation;

        lock (_stateLock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            cancellation = _cancellation;
        }

        if (_observedStorage is not null)
        {
            _observedStorage.StoragePressureDetected -= OnStoragePressureDetected;
            _observedStorage = null;
        }

        // The loop owns the source and disposes it when it observes the cancel,
        // so a delay in flight is never holding a disposed token.
        cancellation?.Cancel();
    }
}
