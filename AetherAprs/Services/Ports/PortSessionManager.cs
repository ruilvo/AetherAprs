// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AetherAprs.Configuration;
using AetherAprs.Models.Aprs.Packets;
using AetherAprs.Services.Contracts;
using Microsoft.Extensions.Logging;

namespace AetherAprs.Services.Ports;

/// <summary>
/// The single owner of running modem sessions.
/// </summary>
internal sealed class PortSessionManager : IPortSessionManager
{
    /// <summary>
    /// How long a stop waits for an outstanding send before it disposes the modem
    /// anyway. A wedged transport must not block port teardown forever.
    /// </summary>
    private static readonly TimeSpan DefaultSendDrainTimeout = TimeSpan.FromSeconds(5);

    private readonly IModemFactory _modemFactory;
    private readonly ILogger<PortSessionManager> _logger;

    // Guards every slot field below and the slot dictionary itself.
    private readonly Lock _stateLock = new();
    private readonly Dictionary<Guid, SessionSlot> _slots = [];

    // Aborts transports that are still opening when the application shuts down.
    // It is deliberately not linked into stops: teardown must always run.
    private readonly CancellationTokenSource _disposalCts = new();

    private bool _disposed;

    public PortSessionManager(IModemFactory modemFactory, ILogger<PortSessionManager> logger)
    {
        ArgumentNullException.ThrowIfNull(modemFactory);
        ArgumentNullException.ThrowIfNull(logger);

        _modemFactory = modemFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public event EventHandler<PortPacketReceivedEventArgs>? PacketReceived;

    /// <inheritdoc />
    public event EventHandler<PortTransportFaultEventArgs>? TransportFaulted;

    /// <inheritdoc />
    public IReadOnlyList<Guid> RunningPortIds
    {
        get
        {
            lock (_stateLock)
            {
                return [.. _slots.Values.Where(slot => slot.Session is not null).Select(slot => slot.PortId)];
            }
        }
    }

    /// <inheritdoc />
    public bool IsRunning(Guid portId)
    {
        lock (_stateLock)
        {
            return _slots.TryGetValue(portId, out var slot) && slot.Session is not null;
        }
    }

    /// <summary>
    /// Ports that still hold runtime state. Exposed to tests so the pruning rule
    /// is asserted directly instead of inferred from timing.
    /// </summary>
    internal int TrackedPortCount
    {
        get
        {
            lock (_stateLock)
            {
                return _slots.Count;
            }
        }
    }

    /// <inheritdoc />
    public async Task<PortSessionStartResult> StartAsync(
        PortConfig port,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(port);

        // The caller's token (session supervision) and disposal both abort a
        // transport that is still opening. BLE/RFCOMM connects can block for many
        // seconds, so the token must reach IKissStreamConnector.ConnectAsync.
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _disposalCts.Token);
        var startToken = linkedCts.Token;

        var slot = AcquireSlot(port.Id);
        var gateHeld = false;
        try
        {
            await slot.Gate.WaitAsync(startToken).ConfigureAwait(false);
            gateHeld = true;

            lock (_stateLock)
            {
                if (slot.Session is not null)
                {
                    _logger.LogDebug("Port {PortId} ({PortName}) is already running.", port.Id, port.Name);
                    return PortSessionStartResult.AlreadyRunning(port.Id);
                }
            }

            return await StartCoreAsync(slot, port, startToken).ConfigureAwait(false);
        }
        finally
        {
            if (gateHeld)
            {
                slot.Gate.Release();
            }

            ReleaseSlot(slot);
        }
    }

    /// <inheritdoc />
    public Task<PortSessionStopResult> StopAsync(
        Guid portId,
        TimeSpan? sendDrainTimeout = null,
        CancellationToken cancellationToken = default) =>
        StopCoreAsync(portId, sendDrainTimeout ?? DefaultSendDrainTimeout, retireSlot: false, cancellationToken);

    /// <inheritdoc />
    public Task<PortSessionStopResult> RemoveAsync(Guid portId, CancellationToken cancellationToken = default) =>
        StopCoreAsync(portId, DefaultSendDrainTimeout, retireSlot: true, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<PortSessionStopResult>> StopAllAsync(
        CancellationToken cancellationToken = default)
    {
        Guid[] portIds;
        lock (_stateLock)
        {
            portIds = [.. _slots.Values.Where(slot => slot.Session is not null).Select(slot => slot.PortId)];
        }

        // One attempt per session: a port that refuses to stop must not leave the
        // remaining ports running, and the caller still learns which ones failed.
        var results = new List<PortSessionStopResult>(portIds.Length);
        foreach (var portId in portIds)
        {
            try
            {
                results.Add(await StopAsync(portId, null, cancellationToken).ConfigureAwait(false));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Stopping port {PortId} failed before its modem could be reached.", portId);
                results.Add(PortSessionStopResult.Failed(portId, ex));
            }
        }

        return results;
    }

    /// <inheritdoc />
    public async Task<PortSessionSendStatus> SendAsync(
        Guid portId,
        AprsPacket packet,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(packet);

        ActiveSession session;
        SessionSlot slot;
        lock (_stateLock)
        {
            if (!_slots.TryGetValue(portId, out var found) || found.Session is null || found.Closing)
            {
                return PortSessionSendStatus.NotRunning;
            }

            slot = found;
            session = found.Session;

            // Take the lease before leaving the lock so a stop that removed the
            // session can never dispose the modem underneath this send.
            slot.ActiveSends++;
        }

        try
        {
            await session.Modem.SendAsync(packet, cancellationToken).ConfigureAwait(false);
            return PortSessionSendStatus.Sent;
        }
        finally
        {
            CompleteSend(slot);
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);

        lock (_stateLock)
        {
            if (_disposed)
            {
                return;
            }
        }

        // Abort transports that are still opening; teardown below uses no token so
        // a shutdown can never be the reason a modem stays running.
        _disposalCts.Cancel();
        await StopAllAsync(CancellationToken.None).ConfigureAwait(false);

        SessionSlot[] slots;
        lock (_stateLock)
        {
            slots = [.. _slots.Values];
            _slots.Clear();
            _disposed = true;
        }

        foreach (var slot in slots)
        {
            slot.Gate.Dispose();
        }

        _logger.LogInformation("Disposed port session manager.");
    }

    private async Task<PortSessionStartResult> StartCoreAsync(
        SessionSlot slot,
        PortConfig port,
        CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var modem = await _modemFactory.CreateAsync(port, cancellationToken).ConfigureAwait(false);

            EventHandler<AprsPacket> packetHandler = (_, packet) => RaisePacketReceived(port.Id, packet);
            EventHandler<Exception> errorHandler = (_, exception) => RaiseTransportFault(port.Id, exception);

            modem.PacketReceived += packetHandler;
            modem.ReceiveError += errorHandler;

            try
            {
                modem.Start();
            }
            catch
            {
                // The factory created the modem's resources, so a failed start must
                // release them here instead of leaking a socket or audio capture.
                modem.PacketReceived -= packetHandler;
                modem.ReceiveError -= errorHandler;
                await modem.DisposeAsync().ConfigureAwait(false);
                throw;
            }

            lock (_stateLock)
            {
                slot.Session = new ActiveSession
                {
                    Modem = modem,
                    PacketHandler = packetHandler,
                    ReceiveErrorHandler = errorHandler
                };
            }

            _logger.LogInformation(
                "Started {TransportName} modem for port {PortName} ({PortId}).",
                modem.TransportName,
                port.Name,
                port.Id);

            return PortSessionStartResult.Started(port.Id, modem.TransportName);
        }
        catch (OperationCanceledException ex) when (_disposalCts.IsCancellationRequested)
        {
            _logger.LogDebug(
                "Start cancelled while the session manager is disposing for port {PortName} ({PortId}).",
                port.Name,
                port.Id);
            return PortSessionStartResult.Cancelled(port.Id, ex);
        }
        catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
        {
            // Supervision cancelled the connect (for example while stopping the
            // session); report cancellation with the token that caused it.
            _logger.LogDebug("Start cancelled for port {PortName} ({PortId}).", port.Name, port.Id);
            return PortSessionStartResult.Cancelled(port.Id, ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start modem for port {PortName} ({PortId}).", port.Name, port.Id);
            return PortSessionStartResult.Failed(port.Id, ex);
        }
    }

    private async Task<PortSessionStopResult> StopCoreAsync(
        Guid portId,
        TimeSpan sendDrainTimeout,
        bool retireSlot,
        CancellationToken cancellationToken)
    {
        var slot = AcquireSlot(portId);
        var gateHeld = false;
        try
        {
            await slot.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            gateHeld = true;

            ActiveSession? session;
            Task? drained = null;
            lock (_stateLock)
            {
                session = slot.Session;
                slot.Session = null;

                if (session is not null)
                {
                    // Refuse new sends and remember whether any are still in flight,
                    // so the modem is not disposed while a send is inside it.
                    slot.Closing = true;
                    if (slot.ActiveSends > 0)
                    {
                        slot.Drained ??= new TaskCompletionSource(
                            TaskCreationOptions.RunContinuationsAsynchronously);
                        drained = slot.Drained.Task;
                    }
                }
            }

            if (session is null)
            {
                return PortSessionStopResult.NotRunning(portId);
            }

            var drainTimedOut = drained is not null &&
                !await WaitForSendsAsync(portId, drained, sendDrainTimeout, cancellationToken).ConfigureAwait(false);

            var error = await TearDownAsync(portId, session, cancellationToken).ConfigureAwait(false);

            lock (_stateLock)
            {
                slot.Closing = false;
                slot.Drained = null;
            }

            return error is null
                ? PortSessionStopResult.Stopped(portId, drainTimedOut)
                : PortSessionStopResult.Failed(portId, error, drainTimedOut);
        }
        finally
        {
            if (gateHeld)
            {
                slot.Gate.Release();
            }

            ReleaseSlot(slot);

            if (retireSlot)
            {
                RetireSlot(portId);
            }
        }
    }

    /// <summary>
    /// Waits for outstanding sends to finish, bounded by the caller's deadline.
    /// Returns <see langword="false"/> when the deadline expired first.
    /// </summary>
    private async Task<bool> WaitForSendsAsync(
        Guid portId,
        Task drained,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);

        try
        {
            await drained.WaitAsync(deadline.Token).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning(
                "Port {PortId} still had a send in flight after {TimeoutSeconds} seconds; disposing its modem anyway.",
                portId,
                timeout.TotalSeconds);
            return false;
        }
    }

    /// <summary>
    /// Stops and disposes a detached session, reporting rather than throwing so the
    /// port is released from the running state no matter how the modem reacts.
    /// </summary>
    private async Task<Exception?> TearDownAsync(
        Guid portId,
        ActiveSession session,
        CancellationToken cancellationToken)
    {
        Exception? error = null;

        try
        {
            session.Modem.PacketReceived -= session.PacketHandler;
            session.Modem.ReceiveError -= session.ReceiveErrorHandler;
            await session.Modem.StopAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            error = ex;
            _logger.LogWarning(ex, "Stopping the modem for port {PortId} failed.", portId);
        }

        try
        {
            // The modem owns every resource the factory built for it, including the
            // KISS stream, so one disposal closes the whole graph.
            await session.Modem.DisposeAsync().AsTask().WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            error ??= ex;
            _logger.LogWarning(ex, "Disposing the modem for port {PortId} failed.", portId);
        }

        if (error is null)
        {
            _logger.LogInformation("Stopped modem for port {PortId}.", portId);
        }

        return error;
    }

    private void CompleteSend(SessionSlot slot)
    {
        TaskCompletionSource? drained = null;
        lock (_stateLock)
        {
            slot.ActiveSends--;
            if (slot.Closing && slot.ActiveSends == 0)
            {
                drained = slot.Drained;
            }
        }

        drained?.TrySetResult();
    }

    private void RaisePacketReceived(Guid portId, AprsPacket packet)
    {
        try
        {
            PacketReceived?.Invoke(this, new PortPacketReceivedEventArgs { PortId = portId, Packet = packet });
        }
        catch (Exception ex)
        {
            // This runs on the modem read loop; a faulty subscriber must not stop
            // reception for the whole port.
            _logger.LogError(ex, "PacketReceived subscriber failed for port {PortId}.", portId);
        }
    }

    private void RaiseTransportFault(Guid portId, Exception exception)
    {
        _logger.LogWarning(exception, "Receive error on port {PortId}.", portId);

        try
        {
            TransportFaulted?.Invoke(this, new PortTransportFaultEventArgs(portId, exception));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TransportFaulted subscriber failed for port {PortId}.", portId);
        }
    }

    /// <summary>
    /// Reserves the port's slot for one operation. The reservation keeps the slot
    /// alive until <see cref="ReleaseSlot"/> runs, so retirement can never dispose a
    /// gate another operation is holding.
    /// </summary>
    private SessionSlot AcquireSlot(Guid portId)
    {
        lock (_stateLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (!_slots.TryGetValue(portId, out var slot))
            {
                slot = new SessionSlot(portId);
                _slots[portId] = slot;
            }

            slot.Users++;
            return slot;
        }
    }

    private void ReleaseSlot(SessionSlot slot)
    {
        lock (_stateLock)
        {
            slot.Users--;
            CollectIfUnusedLocked(slot);
        }
    }

    private void RetireSlot(Guid portId)
    {
        lock (_stateLock)
        {
            if (_slots.TryGetValue(portId, out var slot))
            {
                slot.Retired = true;
                CollectIfUnusedLocked(slot);
            }
        }
    }

    private void CollectIfUnusedLocked(SessionSlot slot)
    {
        if (!slot.Retired || slot.Users > 0 || slot.Session is not null)
        {
            return;
        }

        _slots.Remove(slot.PortId);
        slot.Gate.Dispose();
    }

    private sealed class SessionSlot(Guid portId)
    {
        public Guid PortId { get; } = portId;

        /// <summary>Serializes starts and stops for this port.</summary>
        public SemaphoreSlim Gate { get; } = new(1, 1);

        /// <summary>Operations holding this slot; guarded by the manager's lock.</summary>
        public int Users { get; set; }

        /// <summary>Set when the port was removed; guarded by the manager's lock.</summary>
        public bool Retired { get; set; }

        public ActiveSession? Session { get; set; }

        /// <summary>Sends currently inside the modem; guarded by the manager's lock.</summary>
        public int ActiveSends { get; set; }

        /// <summary>Set while a stop is draining; guarded by the manager's lock.</summary>
        public bool Closing { get; set; }

        /// <summary>Completes when the last outstanding send finishes; guarded by the manager's lock.</summary>
        public TaskCompletionSource? Drained { get; set; }
    }

    private sealed class ActiveSession
    {
        public required IPortModem Modem { get; init; }

        public required EventHandler<AprsPacket> PacketHandler { get; init; }

        public required EventHandler<Exception> ReceiveErrorHandler { get; init; }
    }
}
