// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Data.Mappers;
using AetherAprs.Factories.Packets;
using AetherAprs.Models.Aprs.Packets;
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
public sealed class BulletinService : IBulletinService
{
    /// <summary>APRS bulletin text limit (67 characters) per the APRS specification.</summary>
    private const int AprsBulletinTextMaxLength = 67;

    private readonly IPortService _portService;
    private readonly IPacketStorageService _packetStorageService;
    private readonly IPacketQueryService _packetQueryService;
    private readonly IConfigurationService _configurationService;
    private readonly IAprsPortSettingsResolver _portSettingsResolver;
    private readonly IPacketFactory _packetFactory;
    private readonly ILogger<BulletinService> _logger;

    public BulletinService(
        IPortService portService,
        IPacketStorageService packetStorageService,
        IPacketQueryService packetQueryService,
        IConfigurationService configurationService,
        IAprsPortSettingsResolver portSettingsResolver,
        IPacketFactory packetFactory,
        ILogger<BulletinService> logger)
    {
        _portService = portService;
        _packetStorageService = packetStorageService;
        _packetQueryService = packetQueryService;
        _configurationService = configurationService;
        _portSettingsResolver = portSettingsResolver;
        _packetFactory = packetFactory;
        _logger = logger;
    }

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

        var packet = BuildPacket(name, text);
        await TransmitAndStoreAsync(packet, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<BulletinPacket>> GetBulletinsAsync(CancellationToken cancellationToken = default)
    {
        var records = await _packetQueryService.GetBulletinsAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var result = new List<BulletinPacket>(records.Count);
        foreach (var record in records)
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
        var baseCallsign = _configurationService.Settings.Aprs.Callsign;
        var callsignString = _portSettingsResolver.GetCallsign(baseCallsign);

        return _packetFactory.CreateBulletinPacket(callsignString, name, text);
    }

    private async Task TransmitAndStoreAsync(BulletinPacket packet, CancellationToken cancellationToken = default)
    {
        var txPorts = _portService.Ports.Where(p => p.IsEnabled && p.IsTx).ToList();
        if (txPorts.Count == 0)
        {
            throw new InvalidOperationException("No enabled TX ports available to send the bulletin.");
        }

        foreach (var port in txPorts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _portService.SendPacketAsync(port.Id, packet).ConfigureAwait(false);
            await _packetStorageService.StorePacketAsync(packet, port.Id, isOutbound: true, cancellationToken)
                .ConfigureAwait(false);
            _logger.LogInformation("Sent bulletin {Name} on port {PortName}", packet.Name, port.Name);
        }
    }
}
