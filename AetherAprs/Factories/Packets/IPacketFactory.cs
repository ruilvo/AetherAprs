// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Models;
using AetherAprs.Models.Aprs;
using AetherAprs.Models.Aprs.Packets;

using Geo;

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
    /// <param name="addressee">The destination address (recipient of the message).</param>
    /// <param name="messageText">The message text content.</param>
    /// <param name="messageNumber">Optional message number for acknowledgment tracking.</param>
    /// <returns>A MessagePacket ready for transmission.</returns>
    MessagePacket CreateMessagePacket(
        string sourceCallsign,
        AprsAddress addressee,
        string messageText,
        int? messageNumber = null);

    /// <summary>
    /// Creates an acknowledgment packet for a received message.
    /// </summary>
    /// <param name="sourceCallsign">The source callsign (sender of the ack).</param>
    /// <param name="addressee">The destination address (recipient of the ack).</param>
    /// <param name="messageNumber">The message number being acknowledged.</param>
    /// <returns>A MessageAckPacket configured as an acknowledgment.</returns>
    MessageAckPacket CreateAckPacket(
        string sourceCallsign,
        AprsAddress addressee,
        int messageNumber);

    /// <summary>
    /// Creates a rejection packet for a received message.
    /// </summary>
    /// <param name="sourceCallsign">The source callsign (sender of the reject).</param>
    /// <param name="addressee">The destination address (recipient of the reject).</param>
    /// <param name="messageNumber">The message number being rejected.</param>
    /// <returns>A MessageRejPacket configured as a rejection.</returns>
    MessageRejPacket CreateRejPacket(
        string sourceCallsign,
        AprsAddress addressee,
        int messageNumber);

    /// <summary>
    /// Creates an object report packet for transmission.
    /// </summary>
    /// <param name="sourceCallsign">The source callsign.</param>
    /// <param name="name">The object name (up to 9 characters).</param>
    /// <param name="location">The object's geographic coordinate.</param>
    /// <param name="symbolTable">The APRS symbol table.</param>
    /// <param name="symbolCode">The APRS symbol code.</param>
    /// <param name="isLive">Whether the object is live (<c>true</c>) or killed (<c>false</c>).</param>
    /// <param name="comment">Optional comment text.</param>
    /// <returns>An ObjectPacket ready for transmission.</returns>
    ObjectPacket CreateObjectPacket(
        string sourceCallsign,
        string name,
        Coordinate location,
        SymbolTable symbolTable,
        SymbolCode symbolCode,
        bool isLive = true,
        string? comment = null);

    /// <summary>
    /// Creates an item report packet for transmission.
    /// </summary>
    /// <param name="sourceCallsign">The source callsign.</param>
    /// <param name="name">The item name (3-9 characters).</param>
    /// <param name="location">The item's geographic coordinate.</param>
    /// <param name="symbolTable">The APRS symbol table.</param>
    /// <param name="symbolCode">The APRS symbol code.</param>
    /// <param name="isLive">Whether the item is live (<c>true</c>) or killed (<c>false</c>).</param>
    /// <param name="comment">Optional comment text.</param>
    /// <returns>An ItemPacket ready for transmission.</returns>
    ItemPacket CreateItemPacket(
        string sourceCallsign,
        string name,
        Coordinate location,
        SymbolTable symbolTable,
        SymbolCode symbolCode,
        bool isLive = true,
        string? comment = null);

    /// <summary>
    /// Creates a bulletin packet for transmission.
    /// </summary>
    /// <param name="sourceCallsign">The source callsign.</param>
    /// <param name="name">The bulletin name (the raw addressee, e.g. <c>BLN0SPCL</c>).</param>
    /// <param name="text">The bulletin text.</param>
    /// <param name="messageNumber">Optional message number from the <c>{nn}</c> suffix.</param>
    /// <returns>A BulletinPacket ready for transmission.</returns>
    BulletinPacket CreateBulletinPacket(
        string sourceCallsign,
        string name,
        string text,
        int? messageNumber = null);
}
