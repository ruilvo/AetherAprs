// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Models;
using AetherAprs.Models.Aprs;
using AetherAprs.Models.Aprs.Packets;
using AetherAprs.Services.Configuration;
using System;
using System.Linq;

namespace AetherAprs.Factories.Packets;

/// <summary>
/// Factory for creating APRS packets.
/// </summary>
public sealed class PacketFactory : IPacketFactory
{
    private readonly IConfigurationService _configurationService;
    private const double MetersToFeet = 3.28084;

    public PacketFactory(IConfigurationService configurationService)
    {
        _configurationService = configurationService;
    }

    public PositionPacket CreatePositionPacket(
        LocationData location,
        string callsign,
        SymbolTable symbolTable,
        SymbolCode symbolCode,
        double? course = null,
        string? comment = null)
    {
        if (!Callsign.TryParse(callsign, out var sourceCallsign))
        {
            throw new ArgumentException($"Invalid callsign format: {callsign}", nameof(callsign));
        }

        // Use provided comment or fall back to configured default
        var packetComment = comment;
        if (string.IsNullOrWhiteSpace(packetComment))
        {
            packetComment = _configurationService.Settings.Aprs.BeaconComment;
        }

        // Get digipeater path from settings
        var path = Array.Empty<Callsign>();
        var pathString = _configurationService.Settings.Aprs.DigipeaterPath;
        if (!string.IsNullOrWhiteSpace(pathString))
        {
            path = PathHelper.ParsePath(pathString).ToArray();
        }

        return new PositionPacket
        {
            Source = sourceCallsign,
            Destination = new Callsign("APRS"),
            Path = path,
            Location = location.Location,
            Altitude = location.Altitude.HasValue ? location.Altitude.Value * MetersToFeet : null,
            Course = course,
            Symbol = new Symbol(symbolTable, symbolCode),
            Comment = packetComment,
            Precision = 2
        };
    }

    public MessagePacket CreateMessagePacket(
        string sourceCallsign,
        Callsign addressee,
        string messageText,
        int? messageNumber = null)
    {
        if (!Callsign.TryParse(sourceCallsign, out var source))
        {
            throw new ArgumentException($"Invalid callsign format: {sourceCallsign}", nameof(sourceCallsign));
        }

        // Get digipeater path from settings
        var path = Array.Empty<Callsign>();
        var pathString = _configurationService.Settings.Aprs.DigipeaterPath;
        if (!string.IsNullOrWhiteSpace(pathString))
        {
            path = PathHelper.ParsePath(pathString).ToArray();
        }

        return new MessagePacket
        {
            Source = source,
            Destination = new Callsign("APRS"),
            Path = path,
            Addressee = addressee,
            Text = messageText,
            MessageNumber = messageNumber
        };
    }

    public MessagePacket CreateAckPacket(
        string sourceCallsign,
        Callsign addressee,
        int messageNumber)
    {
        if (!Callsign.TryParse(sourceCallsign, out var source))
        {
            throw new ArgumentException($"Invalid callsign format: {sourceCallsign}", nameof(sourceCallsign));
        }

        // Get digipeater path from settings
        var path = Array.Empty<Callsign>();
        var pathString = _configurationService.Settings.Aprs.DigipeaterPath;
        if (!string.IsNullOrWhiteSpace(pathString))
        {
            path = PathHelper.ParsePath(pathString).ToArray();
        }

        return new MessagePacket
        {
            Source = source,
            Destination = new Callsign("APRS"),
            Path = path,
            Addressee = addressee,
            Text = $"ack{messageNumber}",
            MessageNumber = null
        };
    }

    public MessagePacket CreateRejPacket(
        string sourceCallsign,
        Callsign addressee,
        int messageNumber)
    {
        if (!Callsign.TryParse(sourceCallsign, out var source))
        {
            throw new ArgumentException($"Invalid callsign format: {sourceCallsign}", nameof(sourceCallsign));
        }

        // Get digipeater path from settings
        var path = Array.Empty<Callsign>();
        var pathString = _configurationService.Settings.Aprs.DigipeaterPath;
        if (!string.IsNullOrWhiteSpace(pathString))
        {
            path = PathHelper.ParsePath(pathString).ToArray();
        }

        return new MessagePacket
        {
            Source = source,
            Destination = new Callsign("APRS"),
            Path = path,
            Addressee = addressee,
            Text = $"rej{messageNumber}",
            MessageNumber = null
        };
    }
}
