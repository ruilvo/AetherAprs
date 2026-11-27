// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AetherAprs.Services.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AetherAprs.Services.Ports;

public enum SessionLifecycleState
{
    Stopped,
    Starting,
    Running,
    Degraded,
    Stopping
}

public enum PortSessionState
{
    Stopped,
    Connecting,
    Connected,
    Degraded
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
/// <remarks>
/// The coordinator keeps the operator's intent (<see cref="Intent"/>) apart from
/// the runtime status of each port. Intent is never persisted, so a restarted
/// process comes up disarmed. Runtime transitions are written through a single
/// coalescing writer, and diagnostic timestamps are flushed on a fixed interval
/// instead of once per packet. Stopping always uses an internal deadline: a
/// cancelled caller cannot leave sessions half torn down.
/// </remarks>
public sealed class SessionCoordinator : IAsyncDisposable
{
    private static readonly TimeSpan DefaultInitialBackoff = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan DefaultMaximumBackoff = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan DefaultTeardownTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan DefaultDiagnosticPersistInterval = TimeSpan.FromMinutes(1);

    private readonly ISessionPortController _portController;
    private readonly ISessionStateStore _stateStore;
    private readonly SessionStateWriter _stateWriter;
    private readonly TimeProvider _timeProvider;
    private readonly Func<TimeSpan, CancellationToken, Task> _delayAsync;
    private readonly Func<double> _jitter;
    private readonly TimeSpan _initialBackoff;
    private readonly TimeSpan _maximumBackoff;
    private readonly TimeSpan _teardownTimeout;
    private readonly TimeSpan _diagnosticPersistInterval;
    private readonly IDiagnosticEventSink? _diagnostics;
    private readonly Lock _gate = new();
    private readonly Dictionary<Guid, PortSessionStatus> _ports = [];
    private readonly Dictionary<Guid, Task> _reconnectTasks = [];

    private CancellationTokenSource? _sessionCts;
    private SessionIntent _intent = SessionIntent.Disarmed;
    private ITimer? _diagnosticTimer;
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
        IDiagnosticEventSink? diagnostics = null,
        ILogger<SessionCoordinator>? logger = null,
        TimeSpan? teardownTimeout = null,
        TimeSpan? diagnosticPersistInterval = null)
    {
        ArgumentNullException.ThrowIfNull(portController);
        ArgumentNullException.ThrowIfNull(stateStore);

        _portController = portController;
        _stateStore = stateStore;
        _stateWriter = new SessionStateWriter(
            stateStore,
            logger ?? NullLogger<SessionCoordinator>.Instance);
        _timeProvider = timeProvider ?? TimeProvider.System;
        _delayAsync = delayAsync ?? Task.Delay;
        _jitter = jitter ?? (() => Random.Shared.NextDouble());
        _initialBackoff = initialBackoff ?? DefaultInitialBackoff;
        _maximumBackoff = maximumBackoff ?? DefaultMaximumBackoff;
        _teardownTimeout = teardownTimeout ?? DefaultTeardownTimeout;
        _diagnosticPersistInterval = diagnosticPersistInterval ?? DefaultDiagnosticPersistInterval;
        _diagnostics = diagnostics;

        if (_initialBackoff <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(initialBackoff));
        }

        if (_maximumBackoff < _initialBackoff)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumBackoff));
        }

        if (_teardownTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(teardownTimeout));
        }

        if (_diagnosticPersistInterval < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(diagnosticPersistInterval));
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

    /// <summary>
    /// The operator's desired running set. Disarmed until an explicit start.
    /// </summary>
    public SessionIntent Intent
    {
        get
        {
            lock (_gate)
            {
                return _intent;
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
        SessionSnapshot? snapshot = await _stateStore.LoadAsync(cancellationToken).ConfigureAwait(false);

        lock (_gate)
        {
            _ports.Clear();
            if (snapshot is not null)
            {
                foreach (PortSessionStatus port in snapshot.Ports)
                {
                    _ports[port.PortId] = port with
                    {
                        State = PortSessionState.Stopped,
                        NextRetryUtc = null
                    };
                }
            }

            _intent = SessionIntent.Disarmed;
            _state = SessionLifecycleState.Stopped;
            _initialized = true;
        }

        // Persistence begins with the first real transition, not when the container
        // happened to build the coordinator.
        _stateWriter.Start();
        StartDiagnosticTimer();
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

        IReadOnlyList<Guid> armedPortIds;

        lock (_gate)
        {
            if (!_initialized)
            {
                throw new InvalidOperationException("Session coordinator is not initialized.");
            }

            if (_state is not SessionLifecycleState.Stopped)
            {
                return;
            }

            _intent = SessionIntent.Armed(portIds);
            armedPortIds = _intent.PortIds;
            _sessionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _state = SessionLifecycleState.Starting;
            foreach (Guid portId in armedPortIds)
            {
                _ports[portId] = new PortSessionStatus(
                    portId,
                    PortSession.Fire(PortSessionState.Stopped, PortSessionTrigger.Connect));
            }
        }

        PublishTransition();

        lock (_gate)
        {
            foreach (Guid portId in armedPortIds)
            {
                if (_sessionCts is null || _state is SessionLifecycleState.Stopping or SessionLifecycleState.Stopped)
                {
                    break;
                }

                _reconnectTasks[portId] = ReconnectLoopAsync(portId, _sessionCts.Token);
            }
        }
    }

    /// <summary>
    /// Stops every session and disarms the intent.
    /// </summary>
    /// <remarks>
    /// Stopping is not cancellable by the caller: teardown runs against an internal
    /// deadline so a shutdown that has already cancelled its own token still releases
    /// the transports.
    /// </remarks>
    public async Task StopAsync()
    {
        ThrowIfDisposed();
        await StopCoreAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Completes once the current session snapshot reached the state store.
    /// </summary>
    public Task FlushAsync(CancellationToken cancellationToken = default) =>
        _stateWriter.FlushAsync(cancellationToken);

    public Task ReportReceiveAsync(Guid portId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ApplyPortUpdate(
            portId,
            status => status with { LastReceiveUtc = _timeProvider.GetUtcNow() });
        return Task.CompletedTask;
    }

    public Task ReportTransmitAsync(Guid portId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ApplyPortUpdate(
            portId,
            status => status with { LastTransmitUtc = _timeProvider.GetUtcNow() });
        return Task.CompletedTask;
    }

    public Task ReportPortFaultAsync(
        Guid portId,
        Exception exception,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(exception);
        cancellationToken.ThrowIfCancellationRequested();

        if (!TryApplyPortTransition(
            portId,
            PortSessionTrigger.Faulted,
            status => status with
            {
                Error = exception.Message,
                NextRetryUtc = _timeProvider.GetUtcNow()
            }))
        {
            // A late transport callback from a modem that was already torn down must
            // not resurrect a degraded lifecycle state or a reconnect loop after the
            // operator stopped the session.
            _diagnostics?.Record(
                "session",
                DiagnosticSeverity.Information,
                "Ignored",
                "A transport fault arrived after the session stopped.");
            return Task.CompletedTask;
        }

        lock (_gate)
        {
            if (_sessionCts is null ||
                _state is SessionLifecycleState.Stopping or SessionLifecycleState.Stopped ||
                (_reconnectTasks.TryGetValue(portId, out Task? task) && !task.IsCompleted))
            {
                return Task.CompletedTask;
            }

            _reconnectTasks[portId] = ReconnectLoopAsync(portId, _sessionCts.Token);
        }

        return Task.CompletedTask;
    }

    private async Task StopCoreAsync()
    {
        Task[] reconnectTasks;
        Guid[] ports;

        lock (_gate)
        {
            if (_state == SessionLifecycleState.Stopped)
            {
                return;
            }

            _intent = SessionIntent.Disarmed;
            _state = SessionLifecycleState.Stopping;
            reconnectTasks = [.. _reconnectTasks.Values];
            ports = [.. _ports.Keys];
            _sessionCts?.Cancel();
        }

        using CancellationTokenSource teardown = new(_teardownTimeout);

        try
        {
            await Task.WhenAll(reconnectTasks).WaitAsync(teardown.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Cancellation is the normal exit path for the reconnect loops. A
            // deadline expiry surfaces through the disconnect sweep below.
        }
        catch (Exception exception)
        {
            _diagnostics?.Record(
                "session",
                DiagnosticSeverity.Error,
                "Stopping",
                $"A reconnect loop ended with an error: {exception.Message}");
        }

        foreach (Guid portId in ports)
        {
            try
            {
                await _portController.DisconnectAsync(portId, teardown.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (teardown.IsCancellationRequested)
            {
                _diagnostics?.Record(
                    "session",
                    DiagnosticSeverity.Warning,
                    "Stopping",
                    "The teardown deadline expired while disconnecting ports.");
                break;
            }
            catch (Exception exception)
            {
                _diagnostics?.Record(
                    "session",
                    DiagnosticSeverity.Error,
                    "Stopping",
                    $"A port could not be disconnected: {exception.Message}");
            }
        }

        lock (_gate)
        {
            foreach (Guid portId in ports)
            {
                _ = PortSession.TryFire(
                    _ports[portId].State,
                    PortSessionTrigger.Disconnect,
                    out PortSessionState stopped);

                _ports[portId] = _ports[portId] with
                {
                    State = stopped,
                    NextRetryUtc = null,
                    Error = null
                };
            }

            _reconnectTasks.Clear();
            _sessionCts?.Dispose();
            _sessionCts = null;
            _state = SessionLifecycleState.Stopped;
        }

        PublishTransition();

        try
        {
            await _stateWriter.FlushAsync(teardown.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _diagnostics?.Record(
                "session",
                DiagnosticSeverity.Warning,
                "Stopping",
                "The teardown deadline expired before the final session state was written.");
        }
    }

    private async Task ReconnectLoopAsync(Guid portId, CancellationToken cancellationToken)
    {
        int retryCount = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            DateTimeOffset now = _timeProvider.GetUtcNow();
            _ = TryApplyPortTransition(
                portId,
                PortSessionTrigger.Connect,
                status => status with
                {
                    RetryCount = retryCount,
                    NextRetryUtc = null,
                    Error = null
                });

            try
            {
                await _portController.ConnectAsync(portId, cancellationToken).ConfigureAwait(false);
                _ = TryApplyPortTransition(
                    portId,
                    PortSessionTrigger.Connected,
                    status => status with
                    {
                        RetryCount = 0,
                        NextRetryUtc = null,
                        Error = null
                    });
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                retryCount++;
                TimeSpan delay = CalculateBackoff(retryCount);
                _ = TryApplyPortTransition(
                    portId,
                    PortSessionTrigger.Faulted,
                    status => status with
                    {
                        RetryCount = retryCount,
                        NextRetryUtc = now.Add(delay),
                        Error = ex.Message
                    });

                await _delayAsync(delay, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private TimeSpan CalculateBackoff(int retryCount)
    {
        double exponentialSeconds = Math.Min(
            _maximumBackoff.TotalSeconds,
            _initialBackoff.TotalSeconds * Math.Pow(2, retryCount - 1));
        double jitterFactor = 0.5 + Math.Clamp(_jitter(), 0, 1) * 0.5;
        return TimeSpan.FromSeconds(Math.Min(
            _maximumBackoff.TotalSeconds,
            exponentialSeconds * jitterFactor));
    }

    private void ApplyPortUpdate(
        Guid portId,
        Func<PortSessionStatus, PortSessionStatus> update)
    {
        bool changed;
        lock (_gate)
        {
            if (!_ports.TryGetValue(portId, out PortSessionStatus? current))
            {
                current = new PortSessionStatus(portId, PortSessionState.Stopped);
            }

            PortSessionStatus updated = update(current);
            bool portStateChanged = updated.State != current.State;
            _ports[portId] = updated;

            SessionLifecycleState previousAggregateState = _state;
            UpdateAggregateStateLocked();
            changed = portStateChanged || _state != previousAggregateState;
        }

        // LastReceiveUtc/LastTransmitUtc are diagnostics, not state. Rewriting the
        // whole session file, raising StateChanged and recording a diagnostic for
        // every received packet saturates the disk on a busy APRS-IS feed, so only
        // real transitions are published here. Timestamps travel with
        // the next transition or the periodic diagnostic flush.
        if (changed)
        {
            PublishTransition();
        }
    }

    /// <summary>
    /// Applies a session event to one port, keeping the other fields as they are.
    /// </summary>
    /// <param name="portId">The port whose session changed.</param>
    /// <param name="trigger">The event being recorded.</param>
    /// <param name="update">Additional fields to set alongside the new state.</param>
    /// <returns>
    /// <see langword="false"/> when the event is not legal from the port's current
    /// state, which means the transition already happened elsewhere: a transport
    /// fault arriving after teardown stopped the session must not resurrect it.
    /// </returns>
    private bool TryApplyPortTransition(
        Guid portId,
        PortSessionTrigger trigger,
        Func<PortSessionStatus, PortSessionStatus>? update = null)
    {
        bool changed;
        lock (_gate)
        {
            if (!_ports.TryGetValue(portId, out PortSessionStatus? current))
            {
                current = new PortSessionStatus(portId, PortSessionState.Stopped);
            }

            if (!PortSession.TryFire(current.State, trigger, out PortSessionState next))
            {
                return false;
            }

            PortSessionStatus updated = current with { State = next };
            if (update is not null)
            {
                updated = update(updated);
            }

            _ports[portId] = updated;

            SessionLifecycleState previousAggregateState = _state;
            UpdateAggregateStateLocked();
            changed = _state != previousAggregateState;
        }

        if (changed)
        {
            PublishTransition();
        }

        return true;
    }

    private void UpdateAggregateStateLocked()
    {
        if (_ports.Count == 0)
        {
            _state = SessionLifecycleState.Stopped;
        }
        else if (_state is SessionLifecycleState.Stopping)
        {
            return;
        }
        else if (_ports.Values.All(port => port.State == PortSessionState.Connected))
        {
            _state = SessionLifecycleState.Running;
        }
        else if (_ports.Values.Any(port => port.State is PortSessionState.Degraded))
        {
            _state = SessionLifecycleState.Degraded;
        }
        else
        {
            _state = SessionLifecycleState.Starting;
        }
    }

    private void PublishTransition()
    {
        SessionSnapshot snapshot;
        lock (_gate)
        {
            snapshot = new SessionSnapshot(_state, [.. _ports.Values]);
        }

        _stateWriter.Request(snapshot);

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
            SeverityFor(snapshot.State),
            snapshot.State.ToString(),
            "Session state changed.");
    }

    private void StartDiagnosticTimer()
    {
        if (_diagnosticPersistInterval <= TimeSpan.Zero)
        {
            return;
        }

        _diagnosticTimer?.Dispose();
        _diagnosticTimer = _timeProvider.CreateTimer(
            OnDiagnosticTick,
            null,
            _diagnosticPersistInterval,
            _diagnosticPersistInterval);
    }

    private void OnDiagnosticTick(object? state)
    {
        if (Volatile.Read(ref _disposed))
        {
            return;
        }

        SessionSnapshot snapshot;
        lock (_gate)
        {
            if (!_initialized)
            {
                return;
            }

            snapshot = new SessionSnapshot(_state, [.. _ports.Values]);
        }

        // The latest receive and transmit timestamps are worth one small write per
        // interval even when the lifecycle state did not change.
        _stateWriter.Request(snapshot);
    }

    private static DiagnosticSeverity SeverityFor(SessionLifecycleState state) =>
        state switch
        {
            SessionLifecycleState.Degraded => DiagnosticSeverity.Warning,
            _ => DiagnosticSeverity.Information
        };

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        await StopCoreAsync().ConfigureAwait(false);

        _disposed = true;
        _diagnosticTimer?.Dispose();
        _diagnosticTimer = null;
        await _stateWriter.DisposeAsync().ConfigureAwait(false);
    }
}
