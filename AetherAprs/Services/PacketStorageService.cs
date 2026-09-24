// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Configuration;
using AetherAprs.Data;
using AetherAprs.Models.Aprs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AetherAprs.Services;

/// <summary>
/// Service for persisting and managing APRS packets in the database.
/// </summary>
public interface IPacketStorageService
{
    /// <summary>
    /// Stores an APRS packet in the database.
    /// </summary>
    Task StorePacketAsync(AprsPacket packet, Guid? portId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes packets older than the configured retention period.
    /// </summary>
    Task CleanupOldPacketsAsync(CancellationToken cancellationToken = default);
}

public sealed class PacketStorageService : IPacketStorageService
{
    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;
    private readonly IConfigurationService _configurationService;
    private readonly ILogger<PacketStorageService> _logger;

    public PacketStorageService(
        IDbContextFactory<AppDbContext> dbContextFactory,
        IConfigurationService configurationService,
        ILogger<PacketStorageService> logger)
    {
        _dbContextFactory = dbContextFactory;
        _configurationService = configurationService;
        _logger = logger;
    }

    public async Task StorePacketAsync(AprsPacket packet, Guid? portId, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Attempting to store packet from {Source}, Type: {Type}", packet.Source, packet.GetType().Name);
            
            await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
            
            var record = PacketRecordMapper.ToRecord(packet, portId, DateTimeOffset.UtcNow);
            context.Packets.Add(record);
            
            var savedCount = await context.SaveChangesAsync(cancellationToken);
            
            _logger.LogInformation("Successfully stored {PacketType} packet from {Source} (SaveChanges returned {Count})", 
                record.PacketType, record.Source, savedCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to store packet from {Source}", packet.Source);
            throw; // Re-throw to make errors more visible
        }
    }

    public async Task CleanupOldPacketsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var retentionDays = _configurationService.Settings.Aprs.PacketRetentionDays;
            var cutoffDate = DateTimeOffset.UtcNow.AddDays(-retentionDays);

            await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
            
            var oldPackets = context.Packets.Where(p => p.ReceivedAt < cutoffDate);
            var count = await oldPackets.CountAsync(cancellationToken);

            if (count > 0)
            {
                context.Packets.RemoveRange(oldPackets);
                await context.SaveChangesAsync(cancellationToken);
                
                _logger.LogInformation("Cleaned up {Count} packets older than {Days} days", count, retentionDays);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cleanup old packets");
        }
    }
}
