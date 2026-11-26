// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Configuration;
using AetherAprs.Models.Aprs.Packets;
using AetherAprs.Services.Contracts;
using AetherAprs.Services.Diagnostics;
using AetherAprs.Services.Packets;
using AetherAprs.Services.Platform;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace AetherAprs.Services.Ports;

/// <summary>
/// Owns port configuration, inbound ingest, and digipeat relay. Running modem
/// sessions belong to <see cref="IPortSessionManager"/>; this type asks for starts,
/// stops and sends but never touches a modem.
/// </summary>
public class PortService : IPortService, IPortServiceTelemetry, IPortSessionRuntime, IAsyncDisposable
{
    private readonly IPortRegistry _portRegistry;
    private readonly IPortSessionManager _sessionManager;
    private readonly ILogger<PortService> _logger;
    private readonly IPacketStorageService _packetStorageService;
    private readonly IKeepAliveService _keepAliveService;
    private readonly IDigipeaterService _digipeaterService;

    // Guards _ports. Session state is owned and guarded by the session manager.
    private readonly Lock _stateLock = new();
    private readonly List<PortConfig> _ports;

    private readonly CancellationTokenSource _disposalCts = new();
    private readonly Channel<QueuedPacket> _packetChannel;
    private readonly Task _packetProcessorTask;
    private readonly Channel<DigipeatCandidate> _digipeatChannel;
    private readonly Task _digipeatProcessorTask;
    private readonly int _digipeatQueueCapacity;
    private readonly IDiagnosticEventSink? _diagnosticSink;

    // Inbound drops are counted per ingress port so a saturated or failing ingest
    // path is visible instead of silently losing packets (audit C-05/C-07).
    private readonly ConcurrentDictionary<Guid, long> _droppedPacketCounts = new();
    private long _totalDroppedPackets;
    private bool _portsKeepAliveActive;

    /// <summary>Default number of queued inbound packets before drops are counted.</summary>
    private const int DefaultPacketQueueCapacity = 1024;

    /// <summary>Default number of queued digipeat candidates before drops are counted.</summary>
    private const int DefaultDigipeatQueueCapacity = 256;

    /// <summary>Maximum time one digipeat transmission may occupy the relay consumer.</summary>
    private static readonly TimeSpan DigipeatTransmitTimeout = TimeSpan.FromSeconds(30);

    /// <summary>Total storage attempts for one inbound packet, including the first.</summary>
    private const int StoreAttemptLimit = 3;

    private sealed record QueuedPacket(Guid PortId, AprsPacket Packet);

    private sealed record DigipeatCandidate(Guid SourcePortId, AprsPacket Packet);

    public PortService(
        IPortRegistry portRegistry,
        IPortSessionManager sessionManager,
        ILogger<PortService> logger,
        IPacketStorageService packetStorageService,
        IKeepAliveService keepAliveService,
        IDigipeaterService digipeaterService,
        IDiagnosticEventSink? diagnosticSink = null,
        int packetQueueCapacity = DefaultPacketQueueCapacity,
        int digipeatQueueCapacity = DefaultDigipeatQueueCapacity)
    {
        ArgumentNullException.ThrowIfNull(portRegistry);
        ArgumentNullException.ThrowIfNull(sessionManager);

        _portRegistry = portRegistry;
        _sessionManager = sessionManager;
        _logger = logger;
        _packetStorageService = packetStorageService;
        _keepAliveService = keepAliveService;
        _digipeaterService = digipeaterService;
        _diagnosticSink = diagnosticSink;

        // Bound the queue so a burst cannot grow it unboundedly; the single consumer
        // also guarantees packets are persisted and published in receive order.
        // Policy: the modem read loop must never block, so a saturated queue drops
        // the newest packet and counts it (see RaisePacketReceived).
        _packetChannel = Channel.CreateBounded<QueuedPacket>(new BoundedChannelOptions(packetQueueCapacity)
        {
            SingleReader = true,
            FullMode = BoundedChannelFullMode.Wait
        });
        _packetProcessorTask = Task.Run(ProcessPacketQueueAsync);

        // Digipeat candidates are relayed by their own consumer so a slow RF
        // transmission never holds up ingest for other ports (audit C-06).
        // The producer checks occupancy before writing, so a full queue drops the
        // incoming candidate and counts it instead of silently discarding another.
        _digipeatQueueCapacity = digipeatQueueCapacity;
        _digipeatChannel = Channel.CreateBounded<DigipeatCandidate>(new BoundedChannelOptions(digipeatQueueCapacity)
        {
            SingleReader = true,
            FullMode = BoundedChannelFullMode.DropNewest
        });
        _digipeatProcessorTask = Task.Run(ProcessDigipeatQueueAsync);

        _ports = [];

        _sessionManager.PacketReceived += OnSessionPacketReceived;
        _sessionManager.TransportFaulted += OnSessionTransportFaulted;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var loaded = await LoadPortsAsync(cancellationToken).ConfigureAwait(false);
        lock (_stateLock)
        {
            _ports.Clear();
            _ports.AddRange(loaded);
        }
        PortsChanged?.Invoke(this, EventArgs.Empty);
    }

    public IReadOnlyList<PortConfig> Ports
    {
        get
        {
            lock (_stateLock)
            {
                // Hand out copies: a caller must never be able to mutate the
                // service's authoritative state by editing a published snapshot.
                return [.. _ports.Select(port => port.Clone())];
            }
        }
    }

    public event EventHandler? PortsChanged;

    public event EventHandler<PortPacketReceivedEventArgs>? PacketReceived;

    public event EventHandler<PortPacketTransmittedEventArgs>? PacketTransmitted;

    public event EventHandler<PortTransportFaultEventArgs>? TransportFaulted;

    /// <inheritdoc />
    public long GetDroppedPacketCount(Guid portId) =>
        _droppedPacketCounts.TryGetValue(portId, out var count) ? count : 0;

    /// <summary>Total inbound packets dropped across all ports.</summary>
    public long TotalDroppedPackets => Interlocked.Read(ref _totalDroppedPackets);

    /// <inheritdoc />
    public Task<PortSessionStartResult> StartSessionAsync(Guid portId, CancellationToken cancellationToken)
    {
        var port = FindPortSnapshot(portId);
        if (port is null)
        {
            return Task.FromResult(PortSessionStartResult.UnknownPort(portId));
        }

        return _sessionManager.StartAsync(port, cancellationToken);
    }

    /// <inheritdoc />
    public Task<PortSessionStopResult> StopSessionAsync(Guid portId, CancellationToken cancellationToken) =>
        _sessionManager.StopAsync(portId, cancellationToken: cancellationToken);

    public async Task AddPortAsync(PortConfig port)
    {
        ArgumentNullException.ThrowIfNull(port);

        // Durable-first: never expose a port in memory that was not saved.
        await PersistPortAsync(port, default).ConfigureAwait(false);

        var stored = port.Clone();
        lock (_stateLock)
        {
            _ports.Add(stored);
        }

        PortsChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task UpdatePortAsync(PortConfig updatedPort)
    {
        ArgumentNullException.ThrowIfNull(updatedPort);

        if (FindPort(updatedPort.Id) is null)
        {
            return;
        }

        // Durable-first: the persisted row must match the new settings before
        // any session is torn down or any in-memory snapshot is replaced.
        await PersistPortAsync(updatedPort, default).ConfigureAwait(false);

        bool storedIsEnabled;
        lock (_stateLock)
        {
            var stored = _ports.FirstOrDefault(p => p.Id == updatedPort.Id);
            if (stored is null)
            {
                return;
            }

            storedIsEnabled = stored.IsEnabled;
            CopyPortProperties(updatedPort, stored);
        }

        var wasRunning = _sessionManager.IsRunning(updatedPort.Id);

        if (wasRunning || storedIsEnabled)
        {
            _logger.LogDebug(
                "Restarting port {PortName} ({PortId}) after settings update.",
                updatedPort.Name,
                updatedPort.Id);

            // The running session was built from the previous settings, so it is
            // stopped before the new ones are used; the manager serializes this
            // against any concurrent start.
            await _sessionManager
                .StopAsync(updatedPort.Id, cancellationToken: CancellationToken.None)
                .ConfigureAwait(false);

            if (updatedPort.IsEnabled)
            {
                var start = await _sessionManager
                    .StartAsync(updatedPort.Clone(), CancellationToken.None)
                    .ConfigureAwait(false);

                if (!start.IsRunning && start.Status != PortSessionStartStatus.Cancelled)
                {
                    // The edit is committed and the operator intent is preserved;
                    // the port simply reports degraded until supervision retries.
                    _logger.LogWarning(
                        start.Error,
                        "Port {PortName} ({PortId}) was updated but could not be restarted.",
                        updatedPort.Name,
                        updatedPort.Id);
                }
            }
        }

        await SyncKeepAliveAsync().ConfigureAwait(false);
        PortsChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task RemovePortAsync(Guid id)
    {
        // Stop and forget the runtime state first, so no gate or session outlives
        // the configuration it belonged to.
        await _sessionManager
            .RemoveAsync(id, CancellationToken.None)
            .ConfigureAwait(false);

        // Durable-first: the row must be gone before the port disappears from
        // the in-memory snapshot.
        await DeletePortAsync(id, default).ConfigureAwait(false);

        lock (_stateLock)
        {
            var port = _ports.FirstOrDefault(p => p.Id == id);
            if (port is not null)
            {
                _ports.Remove(port);
            }
        }

        await SyncKeepAliveAsync().ConfigureAwait(false);
        PortsChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task SetPortEnabledAsync(Guid id, bool enabled)
    {
        PortConfig? current;
        lock (_stateLock)
        {
            current = _ports.FirstOrDefault(p => p.Id == id);
        }

        if (current is null)
        {
            return;
        }

        if (current.IsEnabled != enabled)
        {
            // Durable-first: the store is the authority. Only after the write
            // succeeds do we publish the new state to memory, so a failed write
            // can never leave the runtime believing an unpersisted intent is saved.
            var desired = current.WithEnabled(enabled);
            await PersistPortAsync(desired, default).ConfigureAwait(false);

            lock (_stateLock)
            {
                var stored = _ports.FirstOrDefault(p => p.Id == id);
                if (stored is not null)
                {
                    CopyPortProperties(desired, stored);
                }
            }
        }

        // Read the authoritative snapshot after the write so the session starts
        // from exactly what was persisted, never from a stale pre-write copy.
        var authoritative = FindPortSnapshot(id) ?? current;

        // Converge the runtime session to the persisted intent. This part is
        // idempotent: asking for an already-enabled port still guarantees it is
        // running, which supervision and startup both rely on.
        if (enabled)
        {
            var start = await _sessionManager
                .StartAsync(authoritative, CancellationToken.None)
                .ConfigureAwait(false);

            if (!start.IsRunning && start.Status != PortSessionStartStatus.Cancelled)
            {
                // The start failed: roll the installed intent back to disabled,
                // durably first, before reporting the failure to the caller. This
                // always writes, because the failure means the persisted intent
                // cannot be honoured regardless of how it got there.
                await PersistPortAsync(authoritative.WithEnabled(false), default).ConfigureAwait(false);

                lock (_stateLock)
                {
                    var stored = _ports.FirstOrDefault(p => p.Id == id);
                    if (stored is not null)
                    {
                        stored.IsEnabled = false;
                    }
                }

                await SyncKeepAliveAsync().ConfigureAwait(false);
                PortsChanged?.Invoke(this, EventArgs.Empty);
                throw new InvalidOperationException($"Failed to start port '{authoritative.Name}'.");
            }
        }
        else
        {
            await _sessionManager
                .StopAsync(id, cancellationToken: CancellationToken.None)
                .ConfigureAwait(false);
        }

        await SyncKeepAliveAsync().ConfigureAwait(false);
        PortsChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task SetPortShowOnMapAsync(Guid id, bool showOnMap)
    {
        PortConfig? current;
        lock (_stateLock)
        {
            current = _ports.FirstOrDefault(p => p.Id == id);
            if (current is null || current.ShowOnMap == showOnMap)
            {
                return;
            }
        }

        // Durable-first, same ordering rule as SetPortEnabledAsync.
        await PersistPortAsync(current.WithShowOnMap(showOnMap), default).ConfigureAwait(false);

        lock (_stateLock)
        {
            var stored = _ports.FirstOrDefault(p => p.Id == id);
            if (stored is not null)
            {
                stored.ShowOnMap = showOnMap;
            }
        }

        PortsChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task SendPacketAsync(Guid id, AprsPacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);

        if (await _sessionManager
                .SendAsync(id, packet, CancellationToken.None)
                .ConfigureAwait(false) == PortSessionSendStatus.Sent)
        {
            RaisePacketTransmitted(id, packet);
            return;
        }

        // Nothing is running. Only an enabled port may be brought up implicitly,
        // and the manager decides atomically whether that start actually yields a
        // session this send can use.
        var portToStart = FindPortSnapshot(id);
        if (portToStart is null || !portToStart.IsEnabled)
        {
            throw new InvalidOperationException($"Port {id} is not running.");
        }

        var start = await _sessionManager
            .StartAsync(portToStart, CancellationToken.None)
            .ConfigureAwait(false);
        if (!start.IsRunning)
        {
            throw new InvalidOperationException($"Port {id} is not running.", start.Error);
        }

        if (await _sessionManager
                .SendAsync(id, packet, CancellationToken.None)
                .ConfigureAwait(false) != PortSessionSendStatus.Sent)
        {
            throw new InvalidOperationException($"Port {id} is not running.");
        }

        // Auto-starting changed the running set, so the foreground service follows.
        await SyncKeepAliveAsync().ConfigureAwait(false);
        RaisePacketTransmitted(id, packet);
    }

    public async Task StopAllPortsAsync()
    {
        var results = await _sessionManager
            .StopAllAsync(CancellationToken.None)
            .ConfigureAwait(false);

        var failures = results.Count(result => result.Status == PortSessionStopStatus.Failed);
        if (failures > 0)
        {
            _logger.LogWarning(
                "{FailureCount} of {PortCount} ports reported an error while stopping.",
                failures,
                results.Count);
        }

        await SyncKeepAliveAsync().ConfigureAwait(false);
    }

    private PortConfig? FindPort(Guid id)
    {
        lock (_stateLock)
        {
            return _ports.FirstOrDefault(p => p.Id == id);
        }
    }

    /// <summary>
    /// Returns a copy of the configured port, or <see langword="null"/> when it is
    /// not configured.
    /// </summary>
    private PortConfig? FindPortSnapshot(Guid id) => FindPort(id)?.Clone();

    private async Task<List<PortConfig>> LoadPortsAsync(CancellationToken cancellationToken = default) =>
        [.. await _portRegistry.LoadAsync(cancellationToken).ConfigureAwait(false)];

    private Task PersistPortAsync(PortConfig port, CancellationToken cancellationToken = default) =>
        _portRegistry.SaveAsync(port, cancellationToken);

    private Task DeletePortAsync(Guid id, CancellationToken cancellationToken = default) =>
        _portRegistry.DeleteAsync(id, cancellationToken);

    private static void CopyPortProperties(PortConfig source, PortConfig target)
    {
        target.Name = source.Name;
        target.IsEnabled = source.IsEnabled;
        target.IsRx = source.IsRx;
        target.IsTx = source.IsTx;
        target.ShowOnMap = source.ShowOnMap;
        target.AllowDigipeat = source.AllowDigipeat;
        // Copy the settings graph too: the stored instance must never alias the
        // caller's, or a later edit elsewhere would silently change live config.
        target.TypeSettings = source.TypeSettings?.Clone();
    }

    /// <summary>
    /// Keeps the ports keep-alive reason aligned with whether any session is
    /// running.
    /// </summary>
    private async Task SyncKeepAliveAsync()
    {
        var hasRunningSessions = _sessionManager.RunningPortIds.Count > 0;

        if (hasRunningSessions && !_portsKeepAliveActive)
        {
            _portsKeepAliveActive = true;
            await _keepAliveService.AcquireAsync(KeepAliveReason.Ports).ConfigureAwait(false);
            _logger.LogInformation("Acquired keep-alive for ports");
        }
        else if (!hasRunningSessions && _portsKeepAliveActive)
        {
            _portsKeepAliveActive = false;
            await _keepAliveService.ReleaseAsync(KeepAliveReason.Ports).ConfigureAwait(false);
            _logger.LogInformation("Released keep-alive for ports");
        }
    }

    private void RaisePacketTransmitted(Guid id, AprsPacket packet)
    {
        PacketTransmitted?.Invoke(this, new PortPacketTransmittedEventArgs(id, packet));
        _logger.LogDebug("Transmitted APRS packet on port {PortId}, Type={PacketType}.", id, packet.GetType().Name);
    }

    private void OnSessionPacketReceived(object? sender, PortPacketReceivedEventArgs args) =>
        RaisePacketReceived(args.PortId, args.Packet);

    private void OnSessionTransportFaulted(object? sender, PortTransportFaultEventArgs args) =>
        TransportFaulted?.Invoke(this, args);

    private void RaisePacketReceived(Guid portId, AprsPacket packet)
    {
        _logger.LogDebug(
            "Packet received on port {PortId} from {Source}, Type={PacketType}.",
            portId,
            packet.Source,
            packet.GetType().Name);
        if (packet is UnknownPacket)
        {
            _logger.LogDebug(
                "Unknown APRS packet on port {PortId} from {Source}.",
                portId,
                packet.Source);
        }

        // Queue for ordered sequential processing: storage → event, then a
        // separate digipeat consumer. A bounded channel preserves receive order
        // (Id-ordering assumptions in the query service) and prevents unbounded
        // Task.Run fan-out under bursts.
        // TryWrite is intentional: this runs on the modem read loop, which must not
        // block on the database. A full queue therefore drops the newest packet and
        // records it below rather than waiting (audit C-07).
        if (!_packetChannel.Writer.TryWrite(new QueuedPacket(portId, packet)))
        {
            RecordDroppedPacket(portId, packet, "Inbound queue full; newest packet dropped.");
        }
    }

    private async Task ProcessPacketQueueAsync()
    {
        try
        {
            await foreach (var queued in _packetChannel.Reader.ReadAllAsync(_disposalCts.Token).ConfigureAwait(false))
            {
                await ProcessPacketAsync(queued.PortId, queued.Packet).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during disposal
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Packet processing queue terminated unexpectedly.");
        }
    }

    private async Task ProcessDigipeatQueueAsync()
    {
        try
        {
            await foreach (var candidate in _digipeatChannel.Reader.ReadAllAsync(_disposalCts.Token).ConfigureAwait(false))
            {
                try
                {
                    await HandleDigipeatAsync(candidate.SourcePortId, candidate.Packet).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Failed to digipeat packet from {Source} received on port {SourcePortId}.",
                        candidate.Packet.Source,
                        candidate.SourcePortId);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during disposal
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Digipeat queue terminated unexpectedly.");
        }
    }

    private async Task ProcessPacketAsync(Guid portId, AprsPacket packet)
    {
        var stored = await TryStorePacketAsync(portId, packet).ConfigureAwait(false);
        if (!stored)
        {
            // Consumers read the database to render, so publishing an unstored
            // packet would make the UI and the store diverge permanently
            // (audit C-05). Drop it loudly instead.
            RecordDroppedPacket(portId, packet, "Packet could not be persisted.");
            return;
        }

        // Raise event after storage so consumers see persisted packets in order.
        try
        {
            PacketReceived?.Invoke(this, new PortPacketReceivedEventArgs
            {
                PortId = portId,
                Packet = packet
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception in PacketReceived subscriber for packet from {Source}", packet.Source);
        }

        // Handle digipeating on a separate consumer: RF transmit latency must
        // never block ingest of subsequent packets from any port (audit C-06).
        EnqueueDigipeatCandidate(portId, packet);
    }

    /// <summary>
    /// Queues a stored, published packet for the digipeat consumer. Runs on the
    /// ingest consumer, so it never waits for transmit capacity; a full queue
    /// drops the incoming candidate and counts it.
    /// </summary>
    private void EnqueueDigipeatCandidate(Guid portId, AprsPacket packet)
    {
        if (_digipeatChannel.Reader.Count >= _digipeatQueueCapacity)
        {
            RecordDroppedPacket(portId, packet, "Digipeat queue full; candidate dropped.");
            return;
        }

        _digipeatChannel.Writer.TryWrite(new DigipeatCandidate(portId, packet));
    }

    /// <summary>
    /// Persists an inbound packet, retrying transient SQLite contention a bounded
    /// number of times. Returns <see langword="false"/> when the packet must not be
    /// published because it could not be stored.
    /// </summary>
    private async Task<bool> TryStorePacketAsync(Guid portId, AprsPacket packet)
    {
        for (var attempt = 1; attempt <= StoreAttemptLimit; attempt++)
        {
            try
            {
                await _packetStorageService
                    .StorePacketAsync(packet, portId, isOutbound: false, _disposalCts.Token)
                    .ConfigureAwait(false);
                return true;
            }
            catch (OperationCanceledException) when (_disposalCts.IsCancellationRequested)
            {
                // Expected during disposal; nothing to publish.
                return false;
            }
            catch (SqliteException ex) when (attempt < StoreAttemptLimit && IsTransientSqliteFailure(ex))
            {
                _logger.LogWarning(
                    ex,
                    "Transient SQLite failure storing packet from {Source} on port {PortId} (attempt {Attempt}/{Limit}); retrying.",
                    packet.Source,
                    portId,
                    attempt,
                    StoreAttemptLimit);

                if (!await DelayBeforeRetryAsync(attempt).ConfigureAwait(false))
                {
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to store packet from {Source} on port {PortId}; the packet was not published.",
                    packet.Source,
                    portId);
                return false;
            }
        }

        return false;
    }

    private static bool IsTransientSqliteFailure(SqliteException exception) =>
        // SQLITE_BUSY (5) and SQLITE_LOCKED (6) are the two contention codes that
        // clear on their own; anything else is a programming or storage fault.
        exception.SqliteErrorCode is 5 or 6;

    private async Task<bool> DelayBeforeRetryAsync(int attempt)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(50 * attempt), _disposalCts.Token).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private void RecordDroppedPacket(Guid portId, AprsPacket packet, string reason)
    {
        _droppedPacketCounts.AddOrUpdate(portId, 1, static (_, current) => current + 1);
        Interlocked.Increment(ref _totalDroppedPackets);

        _diagnosticSink?.Record(
            "ingest",
            DiagnosticSeverity.Error,
            "Dropped",
            reason,
            portId.ToString("N"));

        _logger.LogWarning(
            "Dropped inbound packet from {Source} on port {PortId}: {Reason}",
            packet.Source,
            portId,
            reason);
    }

    private async Task HandleDigipeatAsync(Guid sourcePortId, AprsPacket packet)
    {
        // Get source port info
        List<PortConfig> configuredPorts;
        lock (_stateLock)
        {
            configuredPorts = [.. _ports];
        }

        var sourcePort = configuredPorts.FirstOrDefault(p => p.Id == sourcePortId);
        if (sourcePort is null)
        {
            return;
        }

        bool sourceIsAprsIs = sourcePort.TypeSettings is AprsIsSettings;

        // Build list of available ports (running and TX-capable). Session
        // ownership lives in the manager, so ask it rather than inspecting state.
        var availablePorts = configuredPorts
            .Where(p => _sessionManager.IsRunning(p.Id))
            .Select(p => new PortInfo(
                p.Id,
                p.TypeSettings is AprsIsSettings,
                p.IsTx,
                p.AllowDigipeat))
            .ToList();

        // Get digipeat targets
        var targets = _digipeaterService.GetDigipeatTargets(
            packet,
            sourcePortId,
            sourceIsAprsIs,
            availablePorts);

        // Send to each target port
        foreach (var target in targets)
        {
            // A wedged modem must not stall the relay consumer forever. The token
            // is linked to disposal and bounds only this transmit; the underlying
            // SendPacketAsync has no cancellation parameter.
            using var transmitCts = CancellationTokenSource.CreateLinkedTokenSource(_disposalCts.Token);
            transmitCts.CancelAfter(DigipeatTransmitTimeout);

            try
            {
                await SendPacketAsync(target.PortId, target.Packet)
                    .WaitAsync(transmitCts.Token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (_disposalCts.IsCancellationRequested)
            {
                // Expected during disposal
                break;
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning(
                    "Digipeat transmit from {Source} to port {TargetPortId} timed out after {TimeoutSeconds} seconds.",
                    packet.Source,
                    target.PortId,
                    DigipeatTransmitTimeout.TotalSeconds);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to digipeat packet from {Source} to port {TargetPortId}",
                    packet.Source,
                    target.PortId);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        _logger.LogInformation("Disposing PortService and stopping all active ports.");

        // Stop packet processing first, then digipeat relay and ports. The CTS is
        // intentionally not disposed: queued callbacks may still reference its
        // token, and disposing it can throw ObjectDisposedException synchronously
        // into modem threads.
        _disposalCts.Cancel();
        _packetChannel.Writer.TryComplete();
        _digipeatChannel.Writer.TryComplete();

        try
        {
            await _packetProcessorTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected during disposal
        }

        try
        {
            await _digipeatProcessorTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected during disposal
        }

        _sessionManager.PacketReceived -= OnSessionPacketReceived;
        _sessionManager.TransportFaulted -= OnSessionTransportFaulted;

        // Stop sessions through the manager that owns them, bounded so a transport
        // that never answers cannot make disposal hang. A port that cannot be
        // reached is reported rather than allowed to block shutdown.
        using (var teardown = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
        {
            var stops = await _sessionManager.StopAllAsync(teardown.Token).ConfigureAwait(false);
            foreach (var failed in stops.Where(stop => stop.Status == PortSessionStopStatus.Failed))
            {
                _logger.LogWarning(
                    failed.Error,
                    "Port {PortId} did not stop cleanly during disposal.",
                    failed.PortId);
            }
        }

        // Release the port keep-alive reason on disposal.
        if (_portsKeepAliveActive)
        {
            _portsKeepAliveActive = false;
            await _keepAliveService.ReleaseAsync(KeepAliveReason.Ports).ConfigureAwait(false);
        }
    }
}
