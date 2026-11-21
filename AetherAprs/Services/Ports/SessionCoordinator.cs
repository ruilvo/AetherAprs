// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AetherAprs.Services.Diagnostics;

namespace AetherAprs.Services.Ports;

public enum SessionLifecycleState
{
    Stopped,
    Starting,
    Running,
    Degraded,
    Stopping,
    Faulted
}

public enum PortSessionState
{
    Stopped,
    Connecting,
    Connected,
    Degraded,
    Faulted
}

public sealed record PortSessionStatus(
    Guid PortId,
    PortSessionState State,
    DateTimeOffset? LastReceiveUtc = null,
    DateTimeOffset? LastTransmitUtc = null,
    int RetryCount = 0,
    DateTimeOffset? NextRetryUtc = null,
    string? Error = null);

public sealed record SessionSnapshot(
    SessionLifecycleState State,
    IReadOnlyList<PortSessionStatus> Ports);

public sealed class SessionStateChangedEventArgs(SessionSnapshot snapshot) : EventArgs
{
    public SessionSnapshot Snapshot { get; } = snapshot;
}

public interface ISessionPortController
{
    Task ConnectAsync(Guid portId, CancellationToken cancellationToken);

    Task DisconnectAsync(Guid portId, CancellationToken cancellationToken);
}

public interface ISessionStateStore
{
    Task<SessionSnapshot?> LoadAsync(CancellationToken cancellationToken);

    Task SaveAsync(SessionSnapshot snapshot, CancellationToken cancellationToken);
}

/// <summary>
/// Coordinates application lifecycle and reconnect work independently of the UI.
/// </summary>
public sealed class SessionCoordinator : IAsyncDisposable
{
    private static readonly TimeSpan DefaultInitialBackoff = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan DefaultMaximumBackoff = TimeSpan.FromMinutes(5);

    private readonly ISessionPortController _portController;
    private readonly ISessionStateStore _stateStore;
    private readonly TimeProvider _timeProvider;
    private readonly Func<TimeSpan, CancellationToken, Task> _delayAsync;
    private readonly Func<double> _jitter;
    private readonly TimeSpan _initialBackoff;
    private readonly TimeSpan _maximumBackoff;
    private readonly IDiagnosticEventSink? _diagnostics;
    private readonly SemaphoreSlim _persistGate = new(1, 1);
    private readonly object _gate = new();
    private readonly Dictionary<Guid, PortSessionStatus> _ports = new();
    private readonly Dictionary<Guid, Task> _reconnectTasks = new();

    private CancellationTokenSource? _sessionCts;
    private SessionLifecycleState _state = SessionLifecycleState.Stopped;
    private bool _initialized;
    private bool _disposed;

    public SessionCoordinator(
        ISessionPortController portController,
        ISessionStateStore stateStore,
        TimeProvider? timeProvider = null,
        Func<TimeSpan, CancellationToken, Task>? delayAsync = null,
        Func<double>? jitter = null,
        TimeSpan? initialBackoff = null,
        TimeSpan? maximumBackoff = null,
        IDiagnosticEventSink? diagnostics = null)
    {
        _portController = portController;
        _stateStore = stateStore;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _delayAsync = delayAsync ?? Task.Delay;
        _jitter = jitter ?? (() => Random.Shared.NextDouble());
        _initialBackoff = initialBackoff ?? DefaultInitialBackoff;
        _maximumBackoff = maximumBackoff ?? DefaultMaximumBackoff;
        _diagnostics = diagnostics;

        if (_initialBackoff <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(initialBackoff));
        }

        if (_maximumBackoff < _initialBackoff)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumBackoff));
        }
    }

    public SessionLifecycleState State
    {
        get
        {
            lock (_gate)
            {
                return _state;
            }
        }
    }

    public event EventHandler<SessionStateChangedEventArgs>? StateChanged;

    public IReadOnlyList<PortSessionStatus> Ports
    {
        get
        {
            lock (_gate)
            {
                return [.. _ports.Values];
            }
        }
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var snapshot = await _stateStore.LoadAsync(cancellationToken).ConfigureAwait(false);

        lock (_gate)
        {
            _ports.Clear();
            if (snapshot is not null)
            {
                foreach (var port in snapshot.Ports)
                {
                    _ports[port.PortId] = port with
                    {
                        State = PortSessionState.Stopped,
                        NextRetryUtc = null
                    };
                }
            }

            _state = SessionLifecycleState.Stopped;
            _initialized = true;
        }
    }

    public async Task StartAsync(
        IReadOnlyCollection<Guid> portIds,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (portIds.Count == 0)
        {
            throw new ArgumentException("At least one port is required.", nameof(portIds));
        }

        lock (_gate)
        {
            if (!_initialized)
            {
                throw new InvalidOperationException("Session coordinator is not initialized.");
            }

            if (_state is not SessionLifecycleState.Stopped and not SessionLifecycleState.Faulted)
            {
                return;
            }

            _sessionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _state = SessionLifecycleState.Starting;
            foreach (var portId in portIds.Distinct())
            {
                _ports[portId] = new PortSessionStatus(portId, PortSessionState.Connecting);
            }
        }

        await PersistAsync(cancellationToken).ConfigureAwait(false);

        lock (_gate)
        {
            foreach (var portId in portIds.Distinct())
            {
                var task = ReconnectLoopAsync(portId, _sessionCts!.Token);
                _reconnectTasks[portId] = task;
            }
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        Task[] reconnectTasks;
        Guid[] ports;

        lock (_gate)
        {
            if (_state == SessionLifecycleState.Stopped)
            {
                return;
            }

            _state = SessionLifecycleState.Stopping;
            reconnectTasks = [.. _reconnectTasks.Values];
            ports = [.. _ports.Keys];
            _sessionCts?.Cancel();
        }

        try
        {
            await Task.WhenAll(reconnectTasks).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Cancellation is the normal stop path.
        }

        foreach (var portId in ports)
        {
            await _portController.DisconnectAsync(portId, cancellationToken).ConfigureAwait(false);
        }

        lock (_gate)
        {
            foreach (var portId in ports)
            {
                _ports[portId] = _ports[portId] with
                {
                    State = PortSessionState.Stopped,
                    NextRetryUtc = null,
                    Error = null
                };
            }

            _reconnectTasks.Clear();
            _sessionCts?.Dispose();
            _sessionCts = null;
            _state = SessionLifecycleState.Stopped;
        }

        await PersistAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task ReportReceiveAsync(Guid portId, CancellationToken cancellationToken = default)
    {
        await UpdatePortAsync(
            portId,
            status => status with { LastReceiveUtc = _timeProvider.GetUtcNow() },
            cancellationToken).ConfigureAwait(false);
    }

    public async Task ReportTransmitAsync(Guid portId, CancellationToken cancellationToken = default)
    {
        await UpdatePortAsync(
            portId,
            status => status with { LastTransmitUtc = _timeProvider.GetUtcNow() },
            cancellationToken).ConfigureAwait(false);
    }

    public async Task ReportPortFaultAsync(
        Guid portId,
        Exception exception,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(exception);
        await UpdatePortAsync(
            portId,
            status => status with
            {
                State = PortSessionState.Degraded,
                Error = exception.Message,
                NextRetryUtc = _timeProvider.GetUtcNow()
            },
            cancellationToken).ConfigureAwait(false);

        lock (_gate)
        {
            if (_sessionCts is null ||
                _state is SessionLifecycleState.Stopping or SessionLifecycleState.Stopped ||
                (_reconnectTasks.TryGetValue(portId, out var task) && !task.IsCompleted))
            {
                return;
            }

            _reconnectTasks[portId] = ReconnectLoopAsync(portId, _sessionCts.Token);
        }
    }

    private async Task ReconnectLoopAsync(Guid portId, CancellationToken cancellationToken)
    {
        var retryCount = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            var now = _timeProvider.GetUtcNow();
            await UpdatePortAsync(
                portId,
                status => status with
                {
                    State = PortSessionState.Connecting,
                    RetryCount = retryCount,
                    NextRetryUtc = null,
                    Error = null
                },
                cancellationToken).ConfigureAwait(false);

            try
            {
                await _portController.ConnectAsync(portId, cancellationToken).ConfigureAwait(false);
                await UpdatePortAsync(
                    portId,
                    status => status with
                    {
                        State = PortSessionState.Connected,
                        RetryCount = 0,
                        NextRetryUtc = null,
                        Error = null
                    },
                    cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                retryCount++;
                var delay = CalculateBackoff(retryCount);
                await UpdatePortAsync(
                    portId,
                    status => status with
                    {
                        State = PortSessionState.Degraded,
                        RetryCount = retryCount,
                        NextRetryUtc = now.Add(delay),
                        Error = ex.Message
                    },
                    cancellationToken).ConfigureAwait(false);

                await _delayAsync(delay, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private TimeSpan CalculateBackoff(int retryCount)
    {
        var exponentialSeconds = Math.Min(
            _maximumBackoff.TotalSeconds,
            _initialBackoff.TotalSeconds * Math.Pow(2, retryCount - 1));
        var jitterFactor = 0.5 + Math.Clamp(_jitter(), 0, 1) * 0.5;
        return TimeSpan.FromSeconds(Math.Min(
            _maximumBackoff.TotalSeconds,
            exponentialSeconds * jitterFactor));
    }

    private async Task UpdatePortAsync(
        Guid portId,
        Func<PortSessionStatus, PortSessionStatus> update,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (!_ports.TryGetValue(portId, out var current))
            {
                current = new PortSessionStatus(portId, PortSessionState.Stopped);
            }

            _ports[portId] = update(current);
            UpdateAggregateStateLocked();
        }

        await PersistAsync(cancellationToken).ConfigureAwait(false);
    }

    private void UpdateAggregateStateLocked()
    {
        if (_ports.Count == 0)
        {
            _state = SessionLifecycleState.Stopped;
        }
        else if (_state is SessionLifecycleState.Stopping or SessionLifecycleState.Faulted)
        {
            return;
        }
        else if (_ports.Values.All(port => port.State == PortSessionState.Connected))
        {
            _state = SessionLifecycleState.Running;
        }
        else if (_ports.Values.Any(port => port.State is PortSessionState.Degraded or PortSessionState.Faulted))
        {
            _state = SessionLifecycleState.Degraded;
        }
        else
        {
            _state = SessionLifecycleState.Starting;
        }
    }

    private async Task PersistAsync(CancellationToken cancellationToken)
    {
        SessionSnapshot snapshot;
        lock (_gate)
        {
            snapshot = new SessionSnapshot(_state, [.. _ports.Values]);
        }

        await _persistGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _stateStore.SaveAsync(snapshot, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _persistGate.Release();
        }

        try
        {
            StateChanged?.Invoke(this, new SessionStateChangedEventArgs(snapshot));
        }
        catch
        {
            // State observers must not interrupt persistence or reconnect work.
        }

        _diagnostics?.Record(
            "session",
            snapshot.State is SessionLifecycleState.Faulted
                ? DiagnosticSeverity.Error
                : snapshot.State is SessionLifecycleState.Degraded
                    ? DiagnosticSeverity.Warning
                    : DiagnosticSeverity.Information,
            snapshot.State.ToString(),
            "Session state persisted.");
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        if (_sessionCts is not null)
        {
            await StopAsync().ConfigureAwait(false);
        }

        _disposed = true;
        _persistGate.Dispose();
    }
}
