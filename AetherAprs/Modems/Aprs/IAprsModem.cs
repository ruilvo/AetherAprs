// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Threading;
using System.Threading.Tasks;
using AetherAprs.Models.Aprs;

namespace AetherAprs.Modems.Aprs;

/// <summary>
/// Shared interface for APRS modems.
/// </summary>
/// <remarks>
/// Implementations include <see cref="AprsRfModem"/> (KISS radio modem)
/// and <see cref="AprsIsModem"/> (APRS-IS TCP modem).
/// </remarks>
public interface IAprsModem
{
    /// <summary>
    /// Raised when a valid APRS packet has been received and parsed.
    /// </summary>
    event EventHandler<AprsPacket>? PacketReceived;

    /// <summary>
    /// Raised when an error occurs during packet reception or parsing.
    /// </summary>
    event EventHandler<Exception>? ReceiveError;

    /// <summary>
    /// Starts the modem's receive loop.
    /// </summary>
    void Start();

    /// <summary>
    /// Stops the modem's receive loop gracefully.
    /// </summary>
    /// <returns>A task that completes when the receive loop has stopped.</returns>
    Task StopAsync();

    /// <summary>
    /// Encodes and sends an APRS packet.
    /// Source and destination callsigns are read from the packet itself.
    /// </summary>
    /// <param name="packet">The APRS packet to send.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>A task that completes when the packet has been sent.</returns>
    Task SendAsync(AprsPacket packet, CancellationToken cancellationToken = default);
}