// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Data;
using AetherAprs.Data.Entities;
using AetherAprs.Data.Maintenance;
using AetherAprs.Data.Mappers;
using AetherAprs.Models.Aprs;
using AetherAprs.Models.Messaging;
using AetherAprs.Services.Diagnostics;
using AetherAprs.Services.Maintenance;
using AetherAprs.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace AetherAprs.Tests.Data.Maintenance;

/// <summary>
/// Exercises the local data clear over a real temporary SQLite database so the
/// batching, the writer serialization and the category boundaries are the ones
/// that ship.
/// </summary>
public sealed class LocalDataMaintenanceServiceTests : IAsyncDisposable
{
    private static readonly DateTime Now = new(2026, 11, 27, 12, 0, 0, DateTimeKind.Utc);

    private readonly TempAppDatabase _database;
    private readonly SqliteRuntimeWriter _writer;
    private readonly BoundedDiagnosticEventStore _diagnostics = new();
    private readonly LocalDataMaintenanceService _service;

    public LocalDataMaintenanceServiceTests()
    {
        _database = TempAppDatabase.CreateEmpty();
        using (AppDbContext context = _database.CreateContext())
        {
            context.Database.EnsureCreated();
        }

        _writer = new SqliteRuntimeWriter(_database.Factory);
        _service = new LocalDataMaintenanceService(
            _writer,
            _diagnostics,
            NullLogger<LocalDataMaintenanceService>.Instance);
    }

    public async ValueTask DisposeAsync()
    {
        await _writer.DisposeAsync();
        _database.Dispose();
    }

    [Fact]
    public async Task ClearPacketHistoryAsync_RemovesNonMessagePackets_AndLeavesOtherCategories()
    {
        await AddPacketsAsync(
            [
                CreatePacket(PacketTypes.Position),
                CreatePacket(PacketTypes.Status),
                CreatePacket(PacketTypes.Message)
            ]);
        await AddOutboxAsync([CreateOutbox(Guid.CreateVersion7(), MessageDeliveryStatus.Pending)]);
        await AddTilesAsync(1);

        LocalDataClearResult result = await _service.ClearPacketHistoryAsync(TestContext.Current.CancellationToken);

        Assert.Equal(LocalDataCategory.PacketHistory, result.Category);
        Assert.Equal(2, result.RemovedRows);

        await using AppDbContext context = _database.CreateContext();
        PacketRecord remaining = Assert.Single(context.Packets);
        Assert.Equal(PacketTypes.Message, remaining.PacketType);
        Assert.Equal(1, await context.OutboxMessages.CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, await context.MapTiles.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ClearPacketHistoryAsync_LeavesMessageAcksAndRejectionsToTheMessageCategory()
    {
        await AddPacketsAsync(
            [
                CreatePacket(PacketTypes.Position),
                CreatePacket(PacketTypes.MessageAck, isOutbound: true),
                CreatePacket(PacketTypes.MessageRej, isOutbound: true)
            ]);

        LocalDataClearResult history = await _service.ClearPacketHistoryAsync(TestContext.Current.CancellationToken);
        LocalDataClearResult messages = await _service.ClearMessagesAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, history.RemovedRows);
        Assert.Equal(2, messages.RemovedRows);

        await using AppDbContext context = _database.CreateContext();
        Assert.Equal(0, await context.Packets.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ClearMessagesAsync_KeepsOpenOutboundHistoryAndItsOutboxRows()
    {
        long inbound = await SeedPacketAsync(CreatePacket(PacketTypes.Message));

        Guid pendingId = Guid.CreateVersion7();
        long pendingHistory = await SeedPacketAsync(
            CreatePacket(PacketTypes.Message, isOutbound: true, MessageDeliveryStatus.Pending, pendingId));
        await AddOutboxAsync([CreateOutbox(pendingId, MessageDeliveryStatus.Pending)]);

        // The outbox is authoritative: this history row claims a terminal state
        // but its durable work is still queued, so it must survive the clear.
        Guid queuedId = Guid.CreateVersion7();
        long queuedHistory = await SeedPacketAsync(
            CreatePacket(PacketTypes.Message, isOutbound: true, MessageDeliveryStatus.Acknowledged, queuedId));
        await AddOutboxAsync([CreateOutbox(queuedId, MessageDeliveryStatus.Queued)]);

        Guid acknowledgedId = Guid.CreateVersion7();
        long acknowledgedHistory = await SeedPacketAsync(
            CreatePacket(PacketTypes.Message, isOutbound: true, MessageDeliveryStatus.Acknowledged, acknowledgedId));
        await AddOutboxAsync([CreateOutbox(acknowledgedId, MessageDeliveryStatus.Acknowledged)]);

        long position = await SeedPacketAsync(CreatePacket(PacketTypes.Position));

        LocalDataClearResult result = await _service.ClearMessagesAsync(TestContext.Current.CancellationToken);

        Assert.Equal(LocalDataCategory.Messages, result.Category);
        Assert.Equal(2, result.RemovedRows);

        await using AppDbContext context = _database.CreateContext();
        long[] remaining = [.. context.Packets.OrderBy(packet => packet.Id).Select(packet => packet.Id)];
        Assert.Equal([pendingHistory, queuedHistory, position], remaining);
        Assert.DoesNotContain(inbound, remaining);
        Assert.DoesNotContain(acknowledgedHistory, remaining);
        Assert.Equal(3, await context.OutboxMessages.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ClearPendingOutboundAsync_RemovesTheOutboxRowsAndTheirPacketHistory()
    {
        Guid pendingId = Guid.CreateVersion7();
        await SeedPacketAsync(
            CreatePacket(PacketTypes.Message, isOutbound: true, MessageDeliveryStatus.Pending, pendingId));
        await AddOutboxAsync([CreateOutbox(pendingId, MessageDeliveryStatus.Pending)]);

        Guid queuedId = Guid.CreateVersion7();
        await SeedPacketAsync(
            CreatePacket(PacketTypes.Message, isOutbound: true, MessageDeliveryStatus.Queued, queuedId));
        await AddOutboxAsync([CreateOutbox(queuedId, MessageDeliveryStatus.Queued)]);

        // A history row that already looks terminal still belongs to open work.
        Guid staleId = Guid.CreateVersion7();
        await SeedPacketAsync(
            CreatePacket(PacketTypes.Message, isOutbound: true, MessageDeliveryStatus.Acknowledged, staleId));
        await AddOutboxAsync([CreateOutbox(staleId, MessageDeliveryStatus.Queued)]);

        Guid acknowledgedId = Guid.CreateVersion7();
        long acknowledgedHistory = await SeedPacketAsync(
            CreatePacket(PacketTypes.Message, isOutbound: true, MessageDeliveryStatus.Acknowledged, acknowledgedId));
        await AddOutboxAsync([CreateOutbox(acknowledgedId, MessageDeliveryStatus.Acknowledged)]);

        long inbound = await SeedPacketAsync(CreatePacket(PacketTypes.Message));
        long position = await SeedPacketAsync(CreatePacket(PacketTypes.Position));

        LocalDataClearResult result = await _service.ClearPendingOutboundAsync(TestContext.Current.CancellationToken);

        Assert.Equal(LocalDataCategory.PendingOutbound, result.Category);

        // Three outbox rows and the three history rows that represent them.
        Assert.Equal(6, result.RemovedRows);

        await using AppDbContext context = _database.CreateContext();
        long[] remaining = [.. context.Packets.OrderBy(packet => packet.Id).Select(packet => packet.Id)];
        Assert.Equal([acknowledgedHistory, inbound, position], remaining);
        Guid[] remainingOutbox = [.. context.OutboxMessages.Select(message => message.Id)];
        Assert.Equal([acknowledgedId], remainingOutbox);
    }

    [Fact]
    public async Task ClearOfflineMapTilesAsync_RemovesTilesOnly()
    {
        await AddTilesAsync(3);
        await SeedPacketAsync(CreatePacket(PacketTypes.Position));
        await AddOutboxAsync([CreateOutbox(Guid.CreateVersion7(), MessageDeliveryStatus.Pending)]);

        LocalDataClearResult result = await _service.ClearOfflineMapTilesAsync(TestContext.Current.CancellationToken);

        Assert.Equal(LocalDataCategory.OfflineMapTiles, result.Category);
        Assert.Equal(3, result.RemovedRows);

        await using AppDbContext context = _database.CreateContext();
        Assert.Equal(0, await context.MapTiles.CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, await context.Packets.CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, await context.OutboxMessages.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ClearOfflineMapTilesAsync_OnAnEmptyCache_ReportsNoRows()
    {
        LocalDataClearResult result = await _service.ClearOfflineMapTilesAsync(TestContext.Current.CancellationToken);

        Assert.Equal(LocalDataCategory.OfflineMapTiles, result.Category);
        Assert.Equal(0, result.RemovedRows);
    }

    [Fact]
    public void ClearDiagnostics_EmptiesTheSinkAndReportsTheRemovedCount()
    {
        for (int index = 0; index < 3; index++)
        {
            _diagnostics.Record(
                "port",
                DiagnosticSeverity.Warning,
                "connecting",
                "The port closed unexpectedly.");
        }

        LocalDataClearResult result = _service.ClearDiagnostics();

        Assert.Equal(LocalDataCategory.Diagnostics, result.Category);
        Assert.Equal(3, result.RemovedRows);
        Assert.Empty(_diagnostics.Events);

        Assert.Equal(0, _service.ClearDiagnostics().RemovedRows);
    }

    [Fact]
    public async Task ClearPacketHistoryAsync_MoreRowsThanOneBatch_RemovesEveryRow()
    {
        int total = LocalDataMaintenanceService.ClearBatchSize * 2 + 7;
        await AddPacketsAsync([.. Enumerable.Range(0, total).Select(_ => CreatePacket(PacketTypes.Position))]);
        await SeedPacketAsync(CreatePacket(PacketTypes.Message));

        LocalDataClearResult result = await _service.ClearPacketHistoryAsync(TestContext.Current.CancellationToken);

        Assert.Equal(total, result.RemovedRows);

        await using AppDbContext context = _database.CreateContext();
        Assert.Equal(1, await context.Packets.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ClearPendingOutboundAsync_MoreRowsThanOneBatch_RemovesEveryOpenRow()
    {
        int total = LocalDataMaintenanceService.ClearBatchSize + 25;
        await AddOutboxAsync(
            [
                .. Enumerable.Range(0, total).Select(index => CreateOutbox(
                    Guid.CreateVersion7(),
                    index % 2 == 0 ? MessageDeliveryStatus.Pending : MessageDeliveryStatus.Queued))
            ]);
        Guid terminal = Guid.CreateVersion7();
        await AddOutboxAsync([CreateOutbox(terminal, MessageDeliveryStatus.Acknowledged)]);

        LocalDataClearResult result = await _service.ClearPendingOutboundAsync(TestContext.Current.CancellationToken);

        Assert.Equal(total, result.RemovedRows);

        await using AppDbContext context = _database.CreateContext();
        Guid[] remaining = [.. context.OutboxMessages.Select(message => message.Id)];
        Assert.Equal([terminal], remaining);
    }

    [Fact]
    public async Task ClearResult_ReportsTheRowsEachCategoryActuallyRemoved()
    {
        await AddPacketsAsync(
            [
                CreatePacket(PacketTypes.Position),
                CreatePacket(PacketTypes.Position),
                CreatePacket(PacketTypes.Status),
                CreatePacket(PacketTypes.Message),
                CreatePacket(PacketTypes.Message, isOutbound: true, MessageDeliveryStatus.Acknowledged)
            ]);
        Guid openId = Guid.CreateVersion7();
        await SeedPacketAsync(
            CreatePacket(PacketTypes.Message, isOutbound: true, MessageDeliveryStatus.Pending, openId));
        await AddOutboxAsync(
            [
                CreateOutbox(openId, MessageDeliveryStatus.Pending),
                CreateOutbox(Guid.CreateVersion7(), MessageDeliveryStatus.Queued),
                CreateOutbox(Guid.CreateVersion7(), MessageDeliveryStatus.Queued)
            ]);
        await AddTilesAsync(4);

        // Packet history: two positions and one status row.
        int packetsBefore = await CountPacketsAsync();
        int packetHistory = (await _service.ClearPacketHistoryAsync(TestContext.Current.CancellationToken)).RemovedRows;
        Assert.Equal(3, packetHistory);
        Assert.Equal(packetsBefore - await CountPacketsAsync(), packetHistory);

        // Messages: the inbound message and the terminal outbound message.
        int messagesBefore = await CountPacketsAsync();
        int messages = (await _service.ClearMessagesAsync(TestContext.Current.CancellationToken)).RemovedRows;
        Assert.Equal(2, messages);
        Assert.Equal(messagesBefore - await CountPacketsAsync(), messages);

        int tiles = (await _service.ClearOfflineMapTilesAsync(TestContext.Current.CancellationToken)).RemovedRows;
        Assert.Equal(4, tiles);

        // Pending outbound: three outbox rows plus the one history row that
        // remains for open work.
        int outboxBefore = await CountOutboxAsync();
        int pendingPacketsBefore = await CountPacketsAsync();
        int pendingOutbound = (await _service.ClearPendingOutboundAsync(TestContext.Current.CancellationToken))
            .RemovedRows;
        Assert.Equal(4, pendingOutbound);
        Assert.Equal(outboxBefore + pendingPacketsBefore, pendingOutbound);

        Assert.Equal(0, await CountPacketsAsync());
        Assert.Equal(0, await CountOutboxAsync());
        await using AppDbContext context = _database.CreateContext();
        Assert.Equal(0, await context.MapTiles.CountAsync(TestContext.Current.CancellationToken));
    }

    private async Task<int> CountPacketsAsync()
    {
        await using AppDbContext context = _database.CreateContext();
        return await context.Packets.CountAsync(TestContext.Current.CancellationToken);
    }

    private async Task<int> CountOutboxAsync()
    {
        await using AppDbContext context = _database.CreateContext();
        return await context.OutboxMessages.CountAsync(TestContext.Current.CancellationToken);
    }

    private async Task<long> SeedPacketAsync(PacketRecord packet)
    {
        await AddPacketsAsync([packet]);
        return packet.Id;
    }

    private async Task AddPacketsAsync(IEnumerable<PacketRecord> packets)
    {
        await using AppDbContext context = _database.CreateContext();
        context.Packets.AddRange(packets);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task AddOutboxAsync(IEnumerable<OutboxMessageRecord> messages)
    {
        await using AppDbContext context = _database.CreateContext();
        context.OutboxMessages.AddRange(messages);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task AddTilesAsync(int count)
    {
        await using AppDbContext context = _database.CreateContext();
        IEnumerable<MapTileRecord> tiles = Enumerable.Range(0, count).Select(index => new MapTileRecord
        {
            ZoomLevel = 10,
            TileColumn = index,
            TileRow = index,
            Data = [1, 2, 3],
            UpdatedAt = Now
        });
        context.MapTiles.AddRange(tiles);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private static PacketRecord CreatePacket(
        string packetType,
        bool isOutbound = false,
        MessageDeliveryStatus? status = null,
        Guid? logicalId = null)
    {
        PacketRecord packet = new()
        {
            SourceBase = "N0CALL",
            SourceSsid = AprsSsid.PrimaryStation,
            DestinationBase = "APRS",
            DestinationSsid = AprsSsid.PrimaryStation,
            PacketType = packetType,
            Timestamp = Now,
            IsOutbound = isOutbound,
            RawInfo = ">",
            RawFrameHex = string.Empty,
            RawPath = "N0CALL>APRS"
        };

        if (string.Equals(packetType, PacketTypes.Message, StringComparison.Ordinal))
        {
            packet.Message = new MessageDataRecord
            {
                AddresseeBase = "K0OTH",
                AddresseeSsid = AprsSsid.HumanPortable,
                Text = "Hello",
                LogicalId = logicalId,
                DeliveryStatus = status
            };
        }

        return packet;
    }

    private static OutboxMessageRecord CreateOutbox(Guid id, MessageDeliveryStatus status) => new()
    {
        Id = id,
        AddresseeBase = "K0OTH",
        AddresseeSsid = AprsSsid.HumanPortable,
        Text = "Hello",
        Status = status,
        AttemptCount = 1,
        CreatedAt = Now,
        ExpiresAt = Now.AddMinutes(15),
        NextAttemptAt = Now.AddSeconds(30),
        UpdatedAt = Now
    };
}
