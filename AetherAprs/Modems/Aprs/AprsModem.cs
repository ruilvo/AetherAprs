// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Threading;
using System.Threading.Tasks;
using AetherAprs.Models.Aprs;
using AetherAprs.Modems.Kiss;

namespace AetherAprs.Modems.Aprs;

/// <summary>
/// APRS-dedicated wrapper over the KISS modem.
///
/// Provides high-level APRS packet send/receive over a KISS modem.
/// Takes a <see cref="KissModem"/> via constructor injection — does NOT
/// instantiate it or own its disposal. Disposing the wrapper only stops
/// the receive loop without disposing the underlying KISS modem.
/// </summary>
public sealed class AprsModem : IAsyncDisposable
{
    private readonly KissModem _kissModem;
    private bool _started;

    /// <summary>
    /// Initializes a new instance of <see cref="AprsModem"/>.
    /// </summary>
    /// <param name="kissModem">The underlying KISS modem. Must not be null.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="kissModem"/> is null.</exception>
    public AprsModem(KissModem kissModem)
    {
        ArgumentNullException.ThrowIfNull(kissModem);
        _kissModem = kissModem;
    }

    /// <summary>
    /// Raised when a valid APRS packet has been received and parsed.
    /// Switch on <see cref="IAprsPacket"/> for the concrete type:
    /// <c>PositionPacket</c>, <c>MessagePacket</c>, <c>StatusPacket</c>,
    /// <c>WeatherPacket</c>, or <c>UnknownPacket</c>.
    /// </summary>
    public event EventHandler<IAprsPacket>? PacketReceived;

    /// <summary>
    /// Raised when an error occurs during packet reception or parsing.
    /// </summary>
    public event EventHandler<Exception>? ReceiveError;

    /// <summary>
    /// Starts the modem's receive loop and hooks the underlying KISS modem events.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown if already started.</exception>
    public void Start()
    {
        if (_started)
        {
            throw new InvalidOperationException("AprsModem is already started.");
        }

        _kissModem.FrameReceived += OnKissFrameReceived;
        _kissModem.ReceiveError += OnKissReceiveError;

        _kissModem.Start();

        _started = true;
    }

    /// <summary>
    /// Stops the modem's receive loop and unhooks KISS modem events.
    /// </summary>
    /// <returns>A task that completes when the receive loop has stopped.</returns>
    public async Task StopAsync()
    {
        if (!_started)
        {
            return;
        }

        _kissModem.FrameReceived -= OnKissFrameReceived;
        _kissModem.ReceiveError -= OnKissReceiveError;

        await _kissModem.StopAsync();

        _started = false;
    }

    /// <summary>
    /// Encodes and sends an <see cref="IAprsPacket"/> over the air.
    /// Dispatches to the correct APRS serializer based on the concrete packet type.
    /// </summary>
    /// <param name="packet">The APRS packet to send.</param>
    /// <param name="source">The source callsign.</param>
    /// <param name="destination">The destination callsign.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>A task that completes when the packet has been written to the stream.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="packet"/> is null.</exception>
    public Task SendAsync(IAprsPacket packet, Callsign source, Callsign destination,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(packet);

        var ax25Data = AprsSerializer.Serialize(packet, source, destination);
        var kissFrame = new KissFrame(0x00, ax25Data);
        return _kissModem.SendAsync(kissFrame, cancellationToken);
    }

    /// <summary>
    /// Stops the receive loop without disposing the underlying KISS modem.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        await StopAsync();
    }

    private void OnKissFrameReceived(object? sender, KissFrame frame)
    {
        if (frame.CommandType != KissCommandType.DataFrame)
        {
            return;
        }

        try
        {
            var packet = AprsParser.ParseFrame(frame.Data);
            PacketReceived?.Invoke(this, packet);
        }
        catch (Exception ex)
        {
            ReceiveError?.Invoke(this, ex);
        }
    }

    private void OnKissReceiveError(object? sender, Exception ex)
    {
        ReceiveError?.Invoke(this, ex);
    }
}