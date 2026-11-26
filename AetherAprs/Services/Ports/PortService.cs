// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Configuration;
using AetherAprs.Models.Aprs.Packets;
using AetherAprs.Services.Contracts;
using AetherAprs.Services.Packets;
using AetherAprs.Services.Platform;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AetherAprs.Services.Ports;

/// <summary>
/// Compatibility façade over the port runtime. Configured ports belong to
/// <see cref="IPortConfigurationService"/>, running sessions to
/// <see cref="IPortSessionManager"/>, the inbound queue to
/// <see cref="IPacketIngestPipeline"/>, and the forwarding queue to
/// <see cref="IDigipeatRouter"/>. This type converges those parts: it applies
/// operator intent, forwards observations to consumers, and keeps the
/// keep-alive reason aligned with the running set.
/// </summary>
public class PortService : IPortService, IPortServiceTelemetry, IPortSessionRuntime, IAsyncDisposable
{
    private readonly IPortConfigurationService _configuration;
    private readonly IPortSessionManager _sessionManager;
    private readonly IPacketIngestPipeline _ingestPipeline;
    private readonly IDigipeatRouter _digipeatRouter;
    private readonly ILogger<PortService> _logger;
    private readonly IKeepAliveService _keepAliveService;

    private bool _portsKeepAliveActive;
    private bool _disposed;

    public PortService(
        IPortConfigurationService configuration,
        IPortSessionManager sessionManager,
        IPacketIngestPipeline ingestPipeline,
        IDigipeatRouter digipeatRouter,
        ILogger<PortService> logger,
        IKeepAliveService keepAliveService)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(sessionManager);
        ArgumentNullException.ThrowIfNull(ingestPipeline);
        ArgumentNullException.ThrowIfNull(digipeatRouter);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(keepAliveService);

        _configuration = configuration;
        _sessionManager = sessionManager;
        _ingestPipeline = ingestPipeline;
        _digipeatRouter = digipeatRouter;
        _logger = logger;
        _keepAliveService = keepAliveService;

        _configuration.PortsChanged += OnPortsChanged;
        _sessionManager.PacketReceived += OnSessionPacketReceived;
        _sessionManager.TransportFaulted += OnSessionTransportFaulted;
        _ingestPipeline.PacketStored += OnPacketStored;
        _digipeatRouter.PacketForwarded += OnPacketForwarded;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default) =>
        await _configuration.InitializeAsync(cancellationToken).ConfigureAwait(false);

    public IReadOnlyList<PortConfig> Ports => _configuration.GetPorts();

    public event EventHandler? PortsChanged;

    public event EventHandler<PortPacketReceivedEventArgs>? PacketReceived;

    public event EventHandler<PortPacketTransmittedEventArgs>? PacketTransmitted;

    public event EventHandler<PortTransportFaultEventArgs>? TransportFaulted;

    /// <inheritdoc />
    public long GetDroppedPacketCount(Guid portId) => _ingestPipeline.GetDroppedPacketCount(portId);

    /// <inheritdoc />
    public long GetDroppedForwardCount(Guid portId) =>
        _digipeatRouter.GetDiscardedCandidateCount(portId);

    /// <summary>Total inbound packets dropped across all ports.</summary>
    public long TotalDroppedPackets => _ingestPipeline.TotalDroppedPackets;

    /// <inheritdoc />
    public Task<PortSessionStartResult> StartSessionAsync(Guid portId, CancellationToken cancellationToken)
    {
        var port = _configuration.Find(portId);
        if (port is null)
        {
            return Task.FromResult(PortSessionStartResult.UnknownPort(portId));
        }

        return _sessionManager.StartAsync(port, cancellationToken);
    }

    /// <inheritdoc />
    public Task<PortSessionStopResult> StopSessionAsync(Guid portId, CancellationToken cancellationToken) =>
        _sessionManager.StopAsync(portId, cancellationToken: cancellationToken);

    public Task AddPortAsync(PortConfig port) => _configuration.AddAsync(port, CancellationToken.None);

    public async Task UpdatePortAsync(PortConfig updatedPort)
    {
        ArgumentNullException.ThrowIfNull(updatedPort);

        var stored = _configuration.Find(updatedPort.Id);
        if (stored is null)
        {
            return;
        }

        var storedIsEnabled = stored.IsEnabled;

        // Ask the manager before the write so the restart decision reflects the
        // session that is actually running, not the newly published intent.
        var wasRunning = _sessionManager.IsRunning(updatedPort.Id);

        if (!await _configuration.UpdateAsync(updatedPort, CancellationToken.None).ConfigureAwait(false))
        {
            return;
        }

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
    }

    public async Task RemovePortAsync(Guid id)
    {
        // Stop and forget the runtime state first, so no gate or session outlives
        // the configuration it belonged to.
        await _sessionManager
            .RemoveAsync(id, CancellationToken.None)
            .ConfigureAwait(false);

        await _configuration.DeleteAsync(id, CancellationToken.None).ConfigureAwait(false);
        await SyncKeepAliveAsync().ConfigureAwait(false);
    }

    public async Task SetPortEnabledAsync(Guid id, bool enabled)
    {
        var current = _configuration.Find(id);
        if (current is null)
        {
            return;
        }

        if (current.IsEnabled != enabled)
        {
            // Durable-first: the store is the authority. Only after the write
            // succeeds does the published snapshot change, so a failed write can
            // never leave the runtime believing an unpersisted intent is saved.
            current = await _configuration
                .SetEnabledAsync(id, enabled, CancellationToken.None)
                .ConfigureAwait(false);

            if (current is null)
            {
                return;
            }
        }

        // Converge the runtime session to the persisted intent. This part is
        // idempotent: asking for an already-enabled port still guarantees it is
        // running, which supervision and startup both rely on.
        if (enabled)
        {
            var start = await _sessionManager
                .StartAsync(current, CancellationToken.None)
                .ConfigureAwait(false);

            if (!start.IsRunning && start.Status != PortSessionStartStatus.Cancelled)
            {
                // The start failed: roll the installed intent back to disabled,
                // durably first, before reporting the failure to the caller. This
                // always writes, because the failure means the persisted intent
                // cannot be honoured regardless of how it got there.
                await _configuration
                    .SetEnabledAsync(id, false, CancellationToken.None)
                    .ConfigureAwait(false);

                await SyncKeepAliveAsync().ConfigureAwait(false);
                throw new InvalidOperationException($"Failed to start port '{current.Name}'.");
            }
        }
        else
        {
            await _sessionManager
                .StopAsync(id, cancellationToken: CancellationToken.None)
                .ConfigureAwait(false);
        }

        await SyncKeepAliveAsync().ConfigureAwait(false);
    }

    public Task SetPortShowOnMapAsync(Guid id, bool showOnMap) =>
        _configuration.SetShowOnMapAsync(id, showOnMap, CancellationToken.None);

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
        var portToStart = _configuration.Find(id);
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

    private void OnPortsChanged(object? sender, EventArgs args) => PortsChanged?.Invoke(this, EventArgs.Empty);

    private void OnSessionPacketReceived(object? sender, PortPacketReceivedEventArgs args) =>
        _ingestPipeline.Accept(args.PortId, args.Packet);

    private void OnSessionTransportFaulted(object? sender, PortTransportFaultEventArgs args) =>
        TransportFaulted?.Invoke(this, args);

    private void OnPacketStored(object? sender, PortPacketReceivedEventArgs args)
    {
        try
        {
            PacketReceived?.Invoke(this, args);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Exception in PacketReceived subscriber for packet from {Source}",
                args.Packet.Source);
        }

        // Forwarding runs on its own consumer: RF transmit latency must never block
        // ingest of subsequent packets from any port (audit C-06).
        _digipeatRouter.Enqueue(args.PortId, args.Packet);
    }

    private void OnPacketForwarded(object? sender, PortPacketTransmittedEventArgs args) =>
        RaisePacketTransmitted(args.PortId, args.Packet);

    private void RaisePacketTransmitted(Guid id, AprsPacket packet)
    {
        PacketTransmitted?.Invoke(this, new PortPacketTransmittedEventArgs(id, packet));
        _logger.LogDebug("Transmitted APRS packet on port {PortId}, Type={PacketType}.", id, packet.GetType().Name);
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

    public async ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);

        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _logger.LogInformation("Disposing PortService and stopping all active ports.");

        // Detach from the producers first so no new packet enters the ingest queue
        // while it is being drained, then drain ingest before the relay so every
        // stored packet has been given to the router, then let the router discard
        // anything still pending rather than replay it later.
        _sessionManager.PacketReceived -= OnSessionPacketReceived;
        _sessionManager.TransportFaulted -= OnSessionTransportFaulted;
        _configuration.PortsChanged -= OnPortsChanged;
        _digipeatRouter.PacketForwarded -= OnPacketForwarded;

        await _ingestPipeline.DisposeAsync().ConfigureAwait(false);
        _ingestPipeline.PacketStored -= OnPacketStored;
        await _digipeatRouter.DisposeAsync().ConfigureAwait(false);

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
