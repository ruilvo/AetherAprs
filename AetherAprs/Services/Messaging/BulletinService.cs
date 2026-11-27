// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Configuration;
using AetherAprs.Data.Entities;
using AetherAprs.Data.Mappers;
using AetherAprs.Factories.Packets;
using AetherAprs.Models.Aprs.Packets;
using AetherAprs.Models.Objects;
using AetherAprs.Services.Configuration;
using AetherAprs.Services.Packets;
using AetherAprs.Services.Ports;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AetherAprs.Services.Messaging;

/// <summary>
/// Sends and lists APRS bulletins. Transmits via <see cref="IPortService"/> and
/// persists via <see cref="IPacketStorageService"/>; reads via <see cref="IPacketQueryService"/>.
/// </summary>
public sealed class BulletinService(
    IPortService portService,
    IPacketStorageService packetStorageService,
    IPacketQueryService packetQueryService,
    IConfigurationService configurationService,
    IAprsPortSettingsResolver portSettingsResolver,
    IPacketFactory packetFactory,
    ILogger<BulletinService> logger) : IBulletinService
{
    /// <summary>APRS bulletin text limit (67 characters) per the APRS specification.</summary>
    private const int AprsBulletinTextMaxLength = 67;

    private readonly IPortService _portService = portService;
    private readonly IPacketStorageService _packetStorageService = packetStorageService;
    private readonly IPacketQueryService _packetQueryService = packetQueryService;
    private readonly IConfigurationService _configurationService = configurationService;
    private readonly IAprsPortSettingsResolver _portSettingsResolver = portSettingsResolver;
    private readonly IPacketFactory _packetFactory = packetFactory;
    private readonly ILogger<BulletinService> _logger = logger;

    public async Task SendAsync(string name, string text, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        name = name.Trim();
        if (!name.StartsWith("BLN", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Bulletin name must start with 'BLN'.", nameof(name));
        }

        if (name.Length > 9)
        {
            throw new ArgumentException("Bulletin name must be 9 characters or fewer.", nameof(name));
        }

        text = text.Trim();
        if (text.Length > AprsBulletinTextMaxLength)
        {
            throw new ArgumentException(
                "APRS bulletin text must be 67 characters or fewer.",
                nameof(text));
        }

        BulletinPacket packet = BuildPacket(name, text);
        await TransmitAndStoreAsync(packet, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<BulletinPacket>> GetBulletinsAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<PacketRecord> records = await _packetQueryService.GetBulletinsAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        List<BulletinPacket> result = [with(records.Count)];
        foreach (PacketRecord record in records)
        {
            if (PacketRecordMapper.MapToPacket(record, _logger) is BulletinPacket packet)
            {
                result.Add(packet);
            }
        }

        return result;
    }

    private BulletinPacket BuildPacket(string name, string text)
    {
        string baseCallsign = _configurationService.Current.Aprs.Callsign;
        string callsignString = _portSettingsResolver.GetCallsign(baseCallsign);

        return _packetFactory.CreateBulletinPacket(callsignString, name, text);
    }

    private async Task TransmitAndStoreAsync(BulletinPacket packet, CancellationToken cancellationToken = default)
    {
        List<PortConfig> txPorts = [.. _portService.Ports.Where(p => p.IsEnabled && p.IsTx)];
        if (txPorts.Count == 0)
        {
            throw new BroadcastFailedException(BroadcastFailure.NoTransmitRoute);
        }

        foreach (PortConfig port in txPorts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _portService.SendPacketAsync(port.Id, packet).ConfigureAwait(false);
            await _packetStorageService.StorePacketAsync(packet, port.Id, isOutbound: true, cancellationToken)
                .ConfigureAwait(false);
            _logger.LogInformation("Sent bulletin {Name} on port {PortName}", packet.Name, port.Name);
        }
    }
}
