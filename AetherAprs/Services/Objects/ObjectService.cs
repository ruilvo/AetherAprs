// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Configuration;
using AetherAprs.Data.Entities;
using AetherAprs.Data.Mappers;
using AetherAprs.Factories.Packets;
using AetherAprs.Models.Aprs;
using AetherAprs.Models.Aprs.Packets;
using AetherAprs.Services.Configuration;
using AetherAprs.Services.Packets;
using AetherAprs.Services.Ports;
using Geo;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AetherAprs.Services.Objects;

/// <summary>
/// Manages the lifecycle of APRS objects: create/update, kill, and list.
/// Transmits via <see cref="IPortService"/> and persists via
/// <see cref="IPacketStorageService"/>; reads via <see cref="IPacketQueryService"/>.
/// </summary>
public sealed class ObjectService(
    IPortService portService,
    IPacketStorageService packetStorageService,
    IPacketQueryService packetQueryService,
    IConfigurationService configurationService,
    IAprsPortSettingsResolver portSettingsResolver,
    IPacketFactory packetFactory,
    ILogger<ObjectService> logger) : IObjectService
{
    private readonly IPortService _portService = portService;
    private readonly IPacketStorageService _packetStorageService = packetStorageService;
    private readonly IPacketQueryService _packetQueryService = packetQueryService;
    private readonly IConfigurationService _configurationService = configurationService;
    private readonly IAprsPortSettingsResolver _portSettingsResolver = portSettingsResolver;
    private readonly IPacketFactory _packetFactory = packetFactory;
    private readonly ILogger<ObjectService> _logger = logger;

    public async Task CreateOrUpdateAsync(
        string name,
        Coordinate location,
        SymbolTable symbolTable,
        SymbolCode symbolCode,
        string? comment = null,
        CancellationToken cancellationToken = default)
    {
        ObjectPacket packet = BuildPacket(name, location, symbolTable, symbolCode, isLive: true, comment);
        await TransmitAndStoreAsync(packet, cancellationToken).ConfigureAwait(false);
    }

    public async Task KillAsync(string name, CancellationToken cancellationToken = default)
    {
        // Find the most recent packet for this object name to reuse its last
        // known position, symbol, and comment on the killed report.
        IReadOnlyList<PacketRecord> objects = await _packetQueryService.GetMostRecentObjectsAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        PacketRecord? existing = objects.FirstOrDefault(p =>
            p.Object != null &&
            string.Equals(p.Object.Name, name, StringComparison.Ordinal));

        if (existing?.Object is null)
        {
            throw new InvalidOperationException($"No object named '{name}' is known.");
        }

        ObjectPacket packet = BuildPacket(
            name,
            existing.Object.Location,
            existing.Object.SymbolTable,
            existing.Object.SymbolCode,
            isLive: false,
            existing.Object.Comment);

        await TransmitAndStoreAsync(packet, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ObjectPacket>> GetObjectsAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<PacketRecord> records = await _packetQueryService.GetMostRecentObjectsAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        List<ObjectPacket> result = [with(records.Count)];
        foreach (PacketRecord record in records)
        {
            if (PacketRecordMapper.MapToPacket(record, _logger) is ObjectPacket packet)
            {
                result.Add(packet);
            }
        }

        return result;
    }

    private ObjectPacket BuildPacket(
        string name,
        Coordinate location,
        SymbolTable symbolTable,
        SymbolCode symbolCode,
        bool isLive,
        string? comment)
    {
        string baseCallsign = _configurationService.Current.Aprs.Callsign;
        string callsignString = _portSettingsResolver.GetCallsign(baseCallsign);

        return _packetFactory.CreateObjectPacket(
            callsignString,
            name,
            location,
            symbolTable,
            symbolCode,
            isLive,
            comment);
    }

    private async Task TransmitAndStoreAsync(ObjectPacket packet, CancellationToken cancellationToken = default)
    {
        List<PortConfig> txPorts = [.. _portService.Ports.Where(p => p.IsEnabled && p.IsTx)];
        if (txPorts.Count == 0)
        {
            throw new InvalidOperationException("No enabled TX ports available to transmit the object.");
        }

        foreach (PortConfig port in txPorts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _portService.SendPacketAsync(port.Id, packet).ConfigureAwait(false);
            await _packetStorageService.StorePacketAsync(packet, port.Id, isOutbound: true, cancellationToken)
                .ConfigureAwait(false);
            _logger.LogInformation(
                "Transmitted {State} object {Name} on port {PortName}",
                packet.IsLive ? "live" : "killed",
                packet.Name,
                port.Name);
        }
    }
}
