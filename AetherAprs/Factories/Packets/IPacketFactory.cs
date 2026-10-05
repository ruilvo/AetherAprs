// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Models;
using AetherAprs.Models.Aprs;
using AetherAprs.Models.Aprs.Packets;

namespace AetherAprs.Factories.Packets;

/// <summary>
/// Factory interface for creating APRS packets.
/// </summary>
public interface IPacketFactory
{
    /// <summary>
    /// Creates a position packet for transmission.
    /// </summary>
    /// <param name="location">The location data.</param>
    /// <param name="callsign">The source callsign (with SSID if applicable).</param>
    /// <param name="symbolTable">The APRS symbol table.</param>
    /// <param name="symbolCode">The APRS symbol code.</param>
    /// <param name="course">Optional course in degrees (0-360).</param>
    /// <param name="comment">Optional comment text. If null, uses configured default.</param>
    /// <returns>A PositionPacket ready for transmission.</returns>
    PositionPacket CreatePositionPacket(
        LocationData location,
        string callsign,
        SymbolTable symbolTable,
        SymbolCode symbolCode,
        double? course = null,
        string? comment = null);

    /// <summary>
    /// Creates a message packet for transmission.
    /// </summary>
    /// <param name="sourceCallsign">The source callsign.</param>
    /// <param name="addressee">The destination callsign (recipient of the message).</param>
    /// <param name="messageText">The message text content.</param>
    /// <param name="messageNumber">Optional message number for acknowledgment tracking.</param>
    /// <returns>A MessagePacket ready for transmission.</returns>
    MessagePacket CreateMessagePacket(
        string sourceCallsign,
        Callsign addressee,
        string messageText,
        int? messageNumber = null);

    /// <summary>
    /// Creates an acknowledgment packet for a received message.
    /// </summary>
    /// <param name="sourceCallsign">The source callsign (sender of the ack).</param>
    /// <param name="addressee">The destination callsign (recipient of the ack).</param>
    /// <param name="messageNumber">The message number being acknowledged.</param>
    /// <returns>A MessagePacket configured as an acknowledgment.</returns>
    MessagePacket CreateAckPacket(
        string sourceCallsign,
        Callsign addressee,
        int messageNumber);

    /// <summary>
    /// Creates a rejection packet for a received message.
    /// </summary>
    /// <param name="sourceCallsign">The source callsign (sender of the reject).</param>
    /// <param name="addressee">The destination callsign (recipient of the reject).</param>
    /// <param name="messageNumber">The message number being rejected.</param>
    /// <returns>A MessagePacket configured as a rejection.</returns>
    MessagePacket CreateRejPacket(
        string sourceCallsign,
        Callsign addressee,
        int messageNumber);
}
