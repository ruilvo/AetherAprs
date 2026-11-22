// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Models;
using AetherAprs.Models.Aprs;
using AetherAprs.Models.Aprs.Packets;
using AetherAprs.Services.Configuration;
using Geo;
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

        var format = _configurationService.Settings.Aprs.PositionFormat;

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
            Format = format,
            Precision = format == PositionFormat.Compressed ? 3 : 2
        };
    }

    public MessagePacket CreateMessagePacket(
        string sourceCallsign,
        AprsAddress addressee,
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

    public MessageAckPacket CreateAckPacket(
        string sourceCallsign,
        AprsAddress addressee,
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

        return new MessageAckPacket
        {
            Source = source,
            Destination = new Callsign("APRS"),
            Path = path,
            Addressee = addressee,
            MessageNumber = messageNumber
        };
    }

    public MessageRejPacket CreateRejPacket(
        string sourceCallsign,
        AprsAddress addressee,
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

        return new MessageRejPacket
        {
            Source = source,
            Destination = new Callsign("APRS"),
            Path = path,
            Addressee = addressee,
            MessageNumber = messageNumber
        };
    }

    public ObjectPacket CreateObjectPacket(
        string sourceCallsign,
        string name,
        Coordinate location,
        SymbolTable symbolTable,
        SymbolCode symbolCode,
        bool isLive = true,
        string? comment = null)
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

        return new ObjectPacket
        {
            Source = source,
            Destination = new Callsign("APRS"),
            Path = path,
            Name = name,
            IsLive = isLive,
            Location = location,
            Symbol = new Symbol(symbolTable, symbolCode),
            Comment = comment,
            Precision = 2
        };
    }

    public ItemPacket CreateItemPacket(
        string sourceCallsign,
        string name,
        Coordinate location,
        SymbolTable symbolTable,
        SymbolCode symbolCode,
        bool isLive = true,
        string? comment = null)
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

        return new ItemPacket
        {
            Source = source,
            Destination = new Callsign("APRS"),
            Path = path,
            Name = name,
            IsLive = isLive,
            Location = location,
            Symbol = new Symbol(symbolTable, symbolCode),
            Comment = comment,
            Precision = 2
        };
    }

    public BulletinPacket CreateBulletinPacket(
        string sourceCallsign,
        string name,
        string text,
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

        return new BulletinPacket
        {
            Source = source,
            Destination = new Callsign("APRS"),
            Path = path,
            Name = name,
            Text = text,
            MessageNumber = messageNumber
        };
    }
}
