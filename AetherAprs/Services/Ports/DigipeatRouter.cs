// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Configuration;
using AetherAprs.Models.Aprs.Packets;
using AetherAprs.Services.Diagnostics;
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
/// Bounded, single-consumer digipeat relay. See <see cref="IDigipeatRouter"/> for
/// the freshness and authorization rules.
/// </summary>
internal sealed class DigipeatRouter : IDigipeatRouter
{
    private readonly IPortConfigurationSource _configurationSource;
    private readonly IPortSessionManager _sessionManager;
    private readonly IDigipeaterService _digipeaterService;
    private readonly ILogger<DigipeatRouter> _logger;
    private readonly IDiagnosticEventSink? _diagnosticSink;
    private readonly TimeProvider _timeProvider;
    private readonly Channel<DigipeatCandidate> _channel;
    private readonly Lock _startLock = new();
    private Task? _consumerTask;

    // Intentionally not disposed: in-flight transmissions may still reference the
    // token, and disposing it can throw ObjectDisposedException into modem threads.
    private readonly CancellationTokenSource _disposalCts = new();

    // Discards are counted per ingress port so a saturated or blocked forwarding
    // path is visible instead of silently losing candidates.
    private readonly ConcurrentDictionary<Guid, long> _discardedCandidates = new();
    private long _enqueuedCandidates;
    private long _forwardedPackets;
    private long _failedTransmissions;
    private long _totalDiscardedCandidates;
    private bool _disposed;

    public DigipeatRouter(
        IPortConfigurationSource configurationSource,
        IPortSessionManager sessionManager,
        IDigipeaterService digipeaterService,
        ILogger<DigipeatRouter> logger,
        IDiagnosticEventSink? diagnosticSink = null,
        DigipeatRouterOptions? options = null,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(configurationSource);
        ArgumentNullException.ThrowIfNull(sessionManager);
        ArgumentNullException.ThrowIfNull(digipeaterService);
        ArgumentNullException.ThrowIfNull(logger);

        Options = Validate(options ?? DigipeatRouterOptions.Default);
        _configurationSource = configurationSource;
        _sessionManager = sessionManager;
        _digipeaterService = digipeaterService;
        _logger = logger;
        _diagnosticSink = diagnosticSink;
        _timeProvider = timeProvider ?? TimeProvider.System;

        // Candidates are relayed by their own consumer so a slow RF transmission
        // never holds up ingest for other ports. TryWrite never blocks,
        // so a full queue discards the incoming candidate and counts it instead of
        // silently replacing another one.
        _channel = Channel.CreateBounded<DigipeatCandidate>(new BoundedChannelOptions(Options.QueueCapacity)
        {
            SingleReader = true,
            FullMode = BoundedChannelFullMode.Wait
        });
    }

    public event EventHandler<PortPacketTransmittedEventArgs>? PacketForwarded;

    public DigipeatRouterOptions Options { get; }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_startLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _consumerTask ??= Task.Run(ConsumeAsync, CancellationToken.None);
        }

        return Task.CompletedTask;
    }

    public long TotalDiscardedCandidates => Interlocked.Read(ref _totalDiscardedCandidates);

    public void Enqueue(Guid sourcePortId, AprsPacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);

        if (_disposed)
        {
            return;
        }

        // Runs on the ingest consumer, so it never waits for transmit capacity.
        if (_channel.Writer.TryWrite(new DigipeatCandidate(sourcePortId, packet, _timeProvider.GetUtcNow())))
        {
            Interlocked.Increment(ref _enqueuedCandidates);
            return;
        }

        Discard(sourcePortId, packet, "Digipeat queue full; candidate dropped.");
    }

    public long GetDiscardedCandidateCount(Guid sourcePortId) =>
        _discardedCandidates.TryGetValue(sourcePortId, out long count) ? count : 0;

    public DigipeatMetrics GetMetrics() => new(
        Interlocked.Read(ref _enqueuedCandidates),
        Interlocked.Read(ref _forwardedPackets),
        Interlocked.Read(ref _totalDiscardedCandidates),
        Interlocked.Read(ref _failedTransmissions));

    private async Task ConsumeAsync()
    {
        try
        {
            await foreach (DigipeatCandidate? candidate in _channel.Reader.ReadAllAsync(_disposalCts.Token).ConfigureAwait(false))
            {
                try
                {
                    await ForwardAsync(candidate).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (_disposalCts.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    _logger.LogError(
                        exception,
                        "Failed to digipeat packet from {Source} received on port {SourcePortId}.",
                        candidate.Packet.Source,
                        candidate.SourcePortId);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during disposal.
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Digipeat queue terminated unexpectedly.");
        }
    }

    private async Task ForwardAsync(DigipeatCandidate candidate)
    {
        // Freshness is checked when the candidate is dequeued: a relay that waited
        // behind a blocked route must be discarded, never replayed.
        TimeSpan age = _timeProvider.GetUtcNow() - candidate.ReceivedAt;
        if (age > Options.MaxCandidateAge)
        {
            Discard(
                candidate.SourcePortId,
                candidate.Packet,
                "Digipeat candidate expired before it could be forwarded.");
            return;
        }

        IReadOnlyList<PortConfig> configuredPorts = _configurationSource.GetPorts();

        PortConfig? sourcePort = configuredPorts.FirstOrDefault(port => port.Id == candidate.SourcePortId);
        if (sourcePort is null)
        {
            Discard(
                candidate.SourcePortId,
                candidate.Packet,
                "Source port is no longer configured; forward discarded.");
            return;
        }

        // Only running sessions can carry a forward. Session ownership lives in the
        // manager, so ask it rather than inspecting state.
        List<PortInfo> availablePorts = [.. configuredPorts
            .Where(port => _sessionManager.IsRunning(port.Id))
            .Select(port => new PortInfo(
                port.Id,
                port.TypeSettings is AprsIsSettings,
                port.IsTx,
                port.AllowDigipeat))];

        IReadOnlyList<DigipeatTarget> targets = _digipeaterService.GetDigipeatTargets(
            candidate.Packet,
            candidate.SourcePortId,
            sourcePort.TypeSettings is AprsIsSettings,
            availablePorts);

        foreach (DigipeatTarget target in targets)
        {
            if (!_sessionManager.IsRunning(target.PortId))
            {
                // Never start a port to satisfy a forward: only operator
                // enablement may bring a port up.
                Discard(
                    candidate.SourcePortId,
                    target.Packet,
                    "Target port is not running; forward discarded.");
                continue;
            }

            await TransmitAsync(candidate, target).ConfigureAwait(false);
        }
    }

    private async Task TransmitAsync(DigipeatCandidate candidate, DigipeatTarget target)
    {
        // A wedged modem must not stall the relay consumer forever. The token is
        // linked to disposal and bounds only this transmit.
        using CancellationTokenSource transmitCts = CancellationTokenSource.CreateLinkedTokenSource(_disposalCts.Token);
        transmitCts.CancelAfter(Options.TransmitTimeout);

        try
        {
            PortSessionSendStatus status = await _sessionManager
                .SendAsync(target.PortId, target.Packet, transmitCts.Token)
                .ConfigureAwait(false);

            if (status != PortSessionSendStatus.Sent)
            {
                Discard(
                    candidate.SourcePortId,
                    target.Packet,
                    "Target port is not running; forward discarded.");
                return;
            }

            Interlocked.Increment(ref _forwardedPackets);
            PacketForwarded?.Invoke(
                this,
                new PortPacketTransmittedEventArgs(target.PortId, target.Packet));
        }
        catch (OperationCanceledException) when (_disposalCts.IsCancellationRequested)
        {
            // Expected during disposal; the candidate is not replayed.
            throw;
        }
        catch (OperationCanceledException)
        {
            Interlocked.Increment(ref _failedTransmissions);
            _logger.LogWarning(
                "Digipeat transmit from {Source} to port {TargetPortId} timed out after {TimeoutSeconds} seconds.",
                candidate.Packet.Source,
                target.PortId,
                Options.TransmitTimeout.TotalSeconds);
        }
        catch (Exception exception)
        {
            Interlocked.Increment(ref _failedTransmissions);
            _diagnosticSink?.Record(
                "digipeat",
                DiagnosticSeverity.Warning,
                "Failed",
                "Digipeat transmission failed.",
                target.PortId.ToString("N"));
            _logger.LogError(
                exception,
                "Failed to digipeat packet from {Source} to port {TargetPortId}",
                candidate.Packet.Source,
                target.PortId);
        }
    }

    private void Discard(Guid sourcePortId, AprsPacket packet, string reason)
    {
        if (_disposed)
        {
            // Shutdown is not a discard: pending candidates are dropped on purpose.
            return;
        }

        _discardedCandidates.AddOrUpdate(sourcePortId, 1, static (_, current) => current + 1);
        Interlocked.Increment(ref _totalDiscardedCandidates);

        _diagnosticSink?.Record(
            "digipeat",
            DiagnosticSeverity.Error,
            "Dropped",
            reason,
            sourcePortId.ToString("N"));

        _logger.LogWarning(
            "Discarded digipeat candidate from {Source} received on port {SourcePortId}: {Reason}",
            packet.Source,
            sourcePortId,
            reason);
    }

    private static DigipeatRouterOptions Validate(DigipeatRouterOptions options)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(options.QueueCapacity, 1, nameof(options.QueueCapacity));
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(
            options.MaxCandidateAge,
            TimeSpan.Zero,
            nameof(options.MaxCandidateAge));
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(
            options.TransmitTimeout,
            TimeSpan.Zero,
            nameof(options.TransmitTimeout));
        return options;
    }

    public async ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);

        Task? consumer;

        lock (_startLock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            consumer = _consumerTask;
        }

        _disposalCts.Cancel();
        _channel.Writer.TryComplete();

        if (consumer is null)
        {
            return;
        }

        try
        {
            await consumer.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected during disposal.
        }
    }

    private sealed record DigipeatCandidate(Guid SourcePortId, AprsPacket Packet, DateTimeOffset ReceivedAt);
}
