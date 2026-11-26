// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Configuration.Settings;
using AetherAprs.Models;
using AetherAprs.Models.Aprs;
using AetherAprs.Models.Aprs.Packets;
using AetherAprs.Services.Configuration;
using Geo;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AetherAprs.Factories.Packets;

/// <summary>
/// Factory for creating APRS packets.
/// </summary>
public sealed class PacketFactory : IPacketFactory
{
    /// <summary>
    /// AX.25 destination carried by every packet this application transmits.
    /// </summary>
    /// <remarks>
    /// By APRS convention the AX.25 destination field holds the software identifier
    /// of the transmitting application, which lets receiving stations and APRS-IS
    /// attribute the packet to its source. The identifier follows the allocation
    /// scheme: the reserved <c>APZ</c> prefix marks an unregistered application and
    /// <c>AEP</c> identifies AetherAprs.
    /// </remarks>
    private const string AprsSoftwareIdentifier = "APZAEP";

    private readonly IConfigurationService _configurationService;

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
        var settings = _configurationService.Current.Aprs;

        return CreatePacket(callsign, settings, (source, destination, path) =>
        {
            // Use provided comment or fall back to configured default
            var packetComment = string.IsNullOrWhiteSpace(comment)
                ? settings.BeaconComment
                : comment;

            var format = settings.PositionFormat;

            return new PositionPacket
            {
                Source = source,
                Destination = destination,
                Path = path,
                Location = location.Location,
                Altitude = location.Altitude.HasValue ? AprsUnits.MetersToFeet(location.Altitude.Value) : null,
                Course = course,
                Symbol = new Symbol(symbolTable, symbolCode),
                Comment = packetComment,
                Format = format,
                Precision = format == PositionFormat.Compressed ? 3 : 2
            };
        });
    }

    public MessagePacket CreateMessagePacket(
        string sourceCallsign,
        AprsAddress addressee,
        string messageText,
        int? messageNumber = null)
    {
        var settings = _configurationService.Current.Aprs;

        return CreatePacket(sourceCallsign, settings, (source, destination, path) => new MessagePacket
        {
            Source = source,
            Destination = destination,
            Path = path,
            Addressee = addressee,
            Text = messageText,
            MessageNumber = messageNumber
        });
    }

    public MessageAckPacket CreateAckPacket(
        string sourceCallsign,
        AprsAddress addressee,
        int messageNumber)
    {
        var settings = _configurationService.Current.Aprs;

        return CreatePacket(sourceCallsign, settings, (source, destination, path) => new MessageAckPacket
        {
            Source = source,
            Destination = destination,
            Path = path,
            Addressee = addressee,
            MessageNumber = messageNumber
        });
    }

    public MessageRejPacket CreateRejPacket(
        string sourceCallsign,
        AprsAddress addressee,
        int messageNumber)
    {
        var settings = _configurationService.Current.Aprs;

        return CreatePacket(sourceCallsign, settings, (source, destination, path) => new MessageRejPacket
        {
            Source = source,
            Destination = destination,
            Path = path,
            Addressee = addressee,
            MessageNumber = messageNumber
        });
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
        AprsNameField.EnsureValid(name, nameof(name));

        var settings = _configurationService.Current.Aprs;

        return CreatePacket(sourceCallsign, settings, (source, destination, path) => new ObjectPacket
        {
            Source = source,
            Destination = destination,
            Path = path,
            Name = name,
            IsLive = isLive,
            Location = location,
            Symbol = new Symbol(symbolTable, symbolCode),
            Comment = comment,
            Precision = 2
        });
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
        AprsNameField.EnsureValid(name, nameof(name));

        var settings = _configurationService.Current.Aprs;

        return CreatePacket(sourceCallsign, settings, (source, destination, path) => new ItemPacket
        {
            Source = source,
            Destination = destination,
            Path = path,
            Name = name,
            IsLive = isLive,
            Location = location,
            Symbol = new Symbol(symbolTable, symbolCode),
            Comment = comment,
            Precision = 2
        });
    }

    public BulletinPacket CreateBulletinPacket(
        string sourceCallsign,
        string name,
        string text,
        int? messageNumber = null)
    {
        AprsNameField.EnsureValid(name, nameof(name));

        var settings = _configurationService.Current.Aprs;

        return CreatePacket(sourceCallsign, settings, (source, destination, path) => new BulletinPacket
        {
            Source = source,
            Destination = destination,
            Path = path,
            Name = name,
            Text = text,
            MessageNumber = messageNumber
        });
    }

    /// <summary>
    /// Builds a packet from the addressing every APRS packet shares: the validated
    /// source callsign, the software-identifier destination, and the configured
    /// digipeater path.
    /// </summary>
    private static T CreatePacket<T>(
        string sourceCallsign,
        AprsSettings settings,
        Func<Callsign, Callsign, IReadOnlyList<Callsign>, T> build)
    {
        var source = ResolveSource(sourceCallsign);

        return build(source, new Callsign(AprsSoftwareIdentifier), ResolvePath(settings));
    }

    private static Callsign ResolveSource(string callsign)
    {
        if (!Callsign.TryParse(callsign, out var source))
        {
            throw new ArgumentException($"Invalid callsign format: {callsign}", nameof(callsign));
        }

        return source;
    }

    private static IReadOnlyList<Callsign> ResolvePath(AprsSettings settings)
    {
        var pathString = settings.DigipeaterPath;

        return string.IsNullOrWhiteSpace(pathString)
            ? Array.Empty<Callsign>()
            : PathHelper.ParsePath(pathString).ToArray();
    }
}
