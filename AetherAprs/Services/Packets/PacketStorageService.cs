// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Data;
using AetherAprs.Data.Mappers;
using AetherAprs.Models.Aprs.Packets;
using AetherAprs.Services.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AetherAprs.Services.Packets;

/// <summary>
/// Service for persisting and managing APRS packets in the database.
/// </summary>
public interface IPacketStorageService
{
    /// <summary>
    /// Stores an APRS packet in the database.
    /// </summary>
    /// <param name="packet">The packet to store.</param>
    /// <param name="portId">The port that received or sent the packet.</param>
    /// <param name="isOutbound">True if this is a sent packet, false if received.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task StorePacketAsync(AprsPacket packet, Guid? portId, bool isOutbound = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes packets older than the configured retention period.
    /// </summary>
    Task CleanupOldPacketsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Raised when a packet is successfully stored in the database.
    /// </summary>
    event EventHandler? PacketStored;
}

public sealed class PacketStorageService(
    IDbContextFactory<AppDbContext> dbContextFactory,
    IConfigurationService configurationService,
    ILogger<PacketStorageService> logger) : IPacketStorageService
{
    public event EventHandler? PacketStored;

    public async Task StorePacketAsync(AprsPacket packet, Guid? portId, bool isOutbound = false, CancellationToken cancellationToken = default)
    {
        try
        {
            logger.LogDebug("Storing {Direction} packet from {Source}, Type: {Type}",
                isOutbound ? "outbound" : "inbound", packet.Source, packet.GetType().Name);

            await using var context = await dbContextFactory.CreateDbContextAsync(cancellationToken);

            var record = PacketRecordMapper.ToRecord(packet, portId, DateTimeOffset.UtcNow, isOutbound);
            context.Packets.Add(record);

            await context.SaveChangesAsync(cancellationToken);

            logger.LogDebug("Stored {Direction} {PacketType} packet from {Source}",
                isOutbound ? "outbound" : "inbound", record.PacketType, record.SourceBase);

            // Raise event after successful storage
            PacketStored?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to store packet from {Source}", packet.Source);
            throw; // Re-throw to make errors more visible
        }
    }

    public async Task CleanupOldPacketsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var retentionDays = configurationService.Settings.Aprs.PacketRetentionDays;
            var cutoffDate = DateTime.UtcNow.AddDays(-retentionDays);

            await using var context = await dbContextFactory.CreateDbContextAsync(cancellationToken);

            // Set-based atomic delete avoids materializing entities and the
            // count/delete race (M3).
            var count = await context.Packets
                .Where(p => p.Timestamp < cutoffDate)
                .ExecuteDeleteAsync(cancellationToken);

            if (count > 0)
            {
                logger.LogInformation("Cleaned up {Count} packets older than {Days} days", count, retentionDays);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to cleanup old packets");
        }
    }
}
