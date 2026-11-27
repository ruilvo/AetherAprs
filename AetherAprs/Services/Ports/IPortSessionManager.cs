// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AetherAprs.Configuration;
using AetherAprs.Models.Aprs.Packets;
using AetherAprs.Services.Contracts;

namespace AetherAprs.Services.Ports;

/// <summary>Outcome of a request to start a port's modem session.</summary>
public enum PortSessionStartStatus
{
    /// <summary>A new session was created and its modem started.</summary>
    Started,

    /// <summary>The port already had a running session; nothing was created.</summary>
    AlreadyRunning,

    /// <summary>The port is not configured, so there is nothing to start.</summary>
    UnknownPort,

    /// <summary>Starting was cancelled before the modem became usable.</summary>
    Cancelled,

    /// <summary>The modem or its transport failed to start.</summary>
    Failed
}

/// <summary>
/// Result of starting a port session, including the original failure when one
/// occurred so callers can decide whether to retry, report, or roll back.
/// </summary>
public sealed class PortSessionStartResult
{
    private PortSessionStartResult(
        Guid portId,
        PortSessionStartStatus status,
        string? transportName = null,
        Exception? error = null)
    {
        PortId = portId;
        Status = status;
        TransportName = transportName;
        Error = error;
    }

    public Guid PortId { get; }

    public PortSessionStartStatus Status { get; }

    /// <summary>Transport that served the session, set when it started.</summary>
    public string? TransportName { get; }

    /// <summary>The original failure or cancellation, when the start did not succeed.</summary>
    public Exception? Error { get; }

    /// <summary><see langword="true"/> when the port is running after the call.</summary>
    public bool IsRunning => Status is PortSessionStartStatus.Started or PortSessionStartStatus.AlreadyRunning;

    public static PortSessionStartResult Started(Guid portId, string transportName) =>
        new(portId, PortSessionStartStatus.Started, transportName);

    public static PortSessionStartResult AlreadyRunning(Guid portId) =>
        new(portId, PortSessionStartStatus.AlreadyRunning);

    public static PortSessionStartResult UnknownPort(Guid portId) =>
        new(portId, PortSessionStartStatus.UnknownPort);

    public static PortSessionStartResult Cancelled(Guid portId, Exception error) =>
        new(portId, PortSessionStartStatus.Cancelled, error: error);

    public static PortSessionStartResult Failed(Guid portId, Exception error) =>
        new(portId, PortSessionStartStatus.Failed, error: error);
}

/// <summary>Outcome of a request to stop a port's modem session.</summary>
public enum PortSessionStopStatus
{
    /// <summary>The session was torn down and its modem disposed.</summary>
    Stopped,

    /// <summary>The port had no running session.</summary>
    NotRunning,

    /// <summary>The session was removed, but stopping or disposing its modem failed.</summary>
    Failed
}

/// <summary>
/// Result of stopping a port session. A failed stop still releases the port, so a
/// later start is never blocked by a modem that refused to shut down cleanly.
/// </summary>
public sealed class PortSessionStopResult
{
    private PortSessionStopResult(
        Guid portId,
        PortSessionStopStatus status,
        Exception? error = null,
        bool sendDrainTimedOut = false)
    {
        PortId = portId;
        Status = status;
        Error = error;
        SendDrainTimedOut = sendDrainTimedOut;
    }

    public Guid PortId { get; }

    public PortSessionStopStatus Status { get; }

    /// <summary>The original failure, when stopping or disposal reported one.</summary>
    public Exception? Error { get; }

    /// <summary>
    /// <see langword="true"/> when an in-flight send did not finish within the
    /// drain deadline and the modem was disposed while that send was outstanding.
    /// </summary>
    public bool SendDrainTimedOut { get; }

    public static PortSessionStopResult Stopped(Guid portId, bool sendDrainTimedOut = false) =>
        new(portId, PortSessionStopStatus.Stopped, sendDrainTimedOut: sendDrainTimedOut);

    public static PortSessionStopResult NotRunning(Guid portId) =>
        new(portId, PortSessionStopStatus.NotRunning);

    public static PortSessionStopResult Failed(Guid portId, Exception error, bool sendDrainTimedOut = false) =>
        new(portId, PortSessionStopStatus.Failed, error, sendDrainTimedOut);
}

/// <summary>Outcome of a send attempt against a port's running session.</summary>
public enum PortSessionSendStatus
{
    /// <summary>The modem accepted the packet.</summary>
    Sent,

    /// <summary>The port had no session able to send, so nothing was written.</summary>
    NotRunning
}

/// <summary>
/// Owns running modem sessions: their lifecycle, start and stop gates, send
/// leases, and disposal.
/// </summary>
/// <remarks>
/// <para>
/// A session is never handed out. Callers can start, stop, and send, so no code
/// outside this type can touch a modem while another call is tearing it down.
/// </para>
/// <para>
/// Starts and stops for one port are serialized by a per-port gate, so two
/// concurrent starts cannot both create a modem. A send takes a lease for the
/// duration of the modem call, and a stop waits for outstanding leases before it
/// disposes the modem, so a send never writes through a disposed transport.
/// </para>
/// </remarks>
public interface IPortSessionManager : IAsyncDisposable
{
    /// <summary>Ports whose modem session is currently running.</summary>
    IReadOnlyList<Guid> RunningPortIds { get; }

    /// <summary>Raised after a packet has been received and parsed by a modem.</summary>
    event EventHandler<PortPacketReceivedEventArgs>? PacketReceived;

    /// <summary>Raised when a running modem reports a receive-side fault.</summary>
    event EventHandler<PortTransportFaultEventArgs>? TransportFaulted;

    /// <summary>Whether the port currently has a running session.</summary>
    bool IsRunning(Guid portId);

    /// <summary>
    /// Number of sessions ever installed for the port. It changes whenever a session
    /// is (re)started, so a consumer can capture it when work is queued and compare it
    /// before transmitting: a mismatch means the work belongs to a connection that is
    /// gone and must not be replayed on the new one. Zero when the port never ran.
    /// </summary>
    long GetSessionGeneration(Guid portId);

    /// <summary>
    /// Starts a session for the port, or reports why it could not be started.
    /// </summary>
    /// <remarks>
    /// The port is configuration, not intent: this method starts a modem but never
    /// persists anything, so supervision can reconnect a port whose persisted
    /// enablement it must not change.
    /// </remarks>
    Task<PortSessionStartResult> StartAsync(PortConfig port, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops the port's session, waiting up to <paramref name="sendDrainTimeout"/>
    /// for in-flight sends before the modem is disposed.
    /// </summary>
    Task<PortSessionStopResult> StopAsync(
        Guid portId,
        TimeSpan? sendDrainTimeout = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops the port's session and discards its per-port runtime state, leaving no
    /// gate or session behind for a port that is no longer configured.
    /// </summary>
    Task<PortSessionStopResult> RemoveAsync(Guid portId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops every running session, attempting all of them and returning one result
    /// per session so failures are reported together instead of aborting the sweep.
    /// </summary>
    Task<IReadOnlyList<PortSessionStopResult>> StopAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a packet through the port's running session.
    /// </summary>
    /// <returns>
    /// <see cref="PortSessionSendStatus.NotRunning"/> when there is no usable
    /// session; transport failures propagate to the caller.
    /// </returns>
    Task<PortSessionSendStatus> SendAsync(
        Guid portId,
        AprsPacket packet,
        CancellationToken cancellationToken = default);
}
