// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using AetherAprs.Data;
using AetherAprs.Data.Entities;
using AetherAprs.Data.Mappers;
using AetherAprs.Data.Packets;
using AetherAprs.Models.Aprs;
using AetherAprs.Services.Packets;
using AetherAprs.Tests.Helpers;

using Geo;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Xunit;

namespace AetherAprs.Tests.Services.Packets;

public sealed class PacketQueryServiceTests : IDisposable
{
    private readonly TempAppDatabase _db;
    private readonly SqliteRuntimeReader _reader;
    private readonly ILogger<PacketQueryService> _logger = NullLogger<PacketQueryService>.Instance;

    public PacketQueryServiceTests()
    {
        _db = TempAppDatabase.CreateEmpty();
        _reader = new SqliteRuntimeReader(_db.Factory);
        using AppDbContext db = _db.CreateContext();
        db.Database.EnsureCreated();
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task GetMostRecentPacketsAsync_EmptyDatabase_ReturnsEmptyDictionary()
    {
        // Arrange
        using PacketQueryService service = new(_reader, _logger);

        // Act
        IReadOnlyDictionary<string, PacketRecord> result = await service.GetMostRecentPacketsAsync(limit: 1000, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public async Task QueryService_RunsWithoutAnAvaloniaApplication()
    {
        // The read capability is an application service: a plain host with no
        // Avalonia application instance must be able to construct it and query.
        Assert.Null(Avalonia.Application.Current);

        using PacketQueryService service = new(_reader, _logger);

        IReadOnlyDictionary<string, PacketRecord> result = await service.GetMostRecentPacketsAsync(
            limit: 10, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Empty(result);
        Assert.Null(Avalonia.Application.Current);
    }

    [Fact]
    public async Task GetMostRecentPacketsAsync_MultiplePacketsFromSameStation_ReturnsOnlyMostRecent()
    {
        // Arrange
        Guid portId = Guid.NewGuid();
        using (AppDbContext context = _db.CreateContext())
        {
            DateTime oldTimestamp = DateTime.UtcNow.AddHours(-2);
            DateTime recentTimestamp = DateTime.UtcNow.AddHours(-1);

            context.Packets.Add(new PacketRecord
            {
                SourceBase = "K0OTH",
                SourceSsid = AprsSsid.AdditionalStation1,
                DestinationBase = "APRS",
                DestinationSsid = AprsSsid.PrimaryStation,
                Timestamp = oldTimestamp,
                PortId = portId,
                PacketType = PacketTypes.Position,
                RawInfo = "!3745.50N/12159.50W-",
                IsOutbound = false,
                Position = new PositionDataRecord
                {
                    Location = new Coordinate(37.758333, -121.991667),
                    Altitude = 150,
                    Course = 90,
                    Speed = 25,
                    SymbolTable = SymbolTable.Primary,
                    SymbolCode = SymbolCode.HyphenMinus,
                    Comment = "Old position"
                }
            });

            context.Packets.Add(new PacketRecord
            {
                SourceBase = "K0OTH",
                SourceSsid = AprsSsid.AdditionalStation1,
                DestinationBase = "APRS",
                DestinationSsid = AprsSsid.PrimaryStation,
                Timestamp = recentTimestamp,
                PortId = portId,
                PacketType = PacketTypes.Position,
                RawInfo = "!3745.51N/12159.51W-",
                IsOutbound = false,
                Position = new PositionDataRecord
                {
                    Location = new Coordinate(37.758350, -121.991683),
                    Altitude = 150,
                    Course = 90,
                    Speed = 25,
                    SymbolTable = SymbolTable.Primary,
                    SymbolCode = SymbolCode.HyphenMinus,
                    Comment = "Recent position"
                }
            });

            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        using PacketQueryService service = new(_reader, _logger);

        // Act
        IReadOnlyDictionary<string, PacketRecord> result = await service.GetMostRecentPacketsAsync(limit: 1000, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(result);
        PacketRecord packet = result["K0OTH-1"];
        Assert.Equal("Recent position", packet.Position?.Comment);
    }

    [Fact]
    public async Task GetMostRecentPacketsAsync_MultipleStations_ReturnsOnePerStation()
    {
        // Arrange
        Guid portId = Guid.NewGuid();
        using (AppDbContext context = _db.CreateContext())
        {
            context.Packets.Add(new PacketRecord
            {
                SourceBase = "K0OTH",
                SourceSsid = AprsSsid.AdditionalStation1,
                DestinationBase = "APRS",
                DestinationSsid = AprsSsid.PrimaryStation,
                Timestamp = DateTime.UtcNow.AddHours(-1),
                PortId = portId,
                PacketType = PacketTypes.Position,
                RawInfo = "!3745.50N/12159.50W-",
                IsOutbound = false,
                Position = new PositionDataRecord
                {
                    Location = new Coordinate(37.758333, -121.991667),
                    SymbolTable = SymbolTable.Primary,
                    SymbolCode = SymbolCode.HyphenMinus
                }
            });

            context.Packets.Add(new PacketRecord
            {
                SourceBase = "WX0TST",
                SourceSsid = AprsSsid.PrimaryStation,
                DestinationBase = "APRS",
                DestinationSsid = AprsSsid.PrimaryStation,
                Timestamp = DateTime.UtcNow.AddMinutes(-30),
                PortId = portId,
                PacketType = PacketTypes.Weather,
                RawInfo = "_03160227c...",
                IsOutbound = false,
                Weather = new WeatherDataRecord
                {
                    Temperature = 21,
                    Humidity = 65
                }
            });

            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        using PacketQueryService service = new(_reader, _logger);

        // Act
        IReadOnlyDictionary<string, PacketRecord> result = await service.GetMostRecentPacketsAsync(limit: 1000, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Contains("K0OTH-1", result.Keys);
        Assert.Contains("WX0TST", result.Keys);
    }

    [Fact]
    public async Task GetMostRecentPacketsAsync_EnforcesLimitParameter()
    {
        // Arrange
        Guid portId = Guid.NewGuid();
        using (AppDbContext context = _db.CreateContext())
        {
            for (int i = 0; i < 5; i++)
            {
                context.Packets.Add(new PacketRecord
                {
                    SourceBase = $"CALL{i}",
                    SourceSsid = AprsSsid.PrimaryStation,
                    DestinationBase = "APRS",
                    DestinationSsid = AprsSsid.PrimaryStation,
                    Timestamp = DateTime.UtcNow.AddHours(-i),
                    PortId = portId,
                    PacketType = PacketTypes.Position,
                    RawInfo = "!3745.50N/12159.50W-",
                    IsOutbound = false,
                    Position = new PositionDataRecord
                    {
                        Location = new Coordinate(37.758333, -121.991667),
                        SymbolTable = SymbolTable.Primary,
                        SymbolCode = SymbolCode.HyphenMinus
                    }
                });
            }
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        using PacketQueryService service = new(_reader, _logger);

        // Act
        IReadOnlyDictionary<string, PacketRecord> result = await service.GetMostRecentPacketsAsync(limit: 3, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(3, result.Count);
    }

    [Fact]
    public async Task GetMostRecentPositionPacketsAsync_FiltersNonPositionPackets()
    {
        // Arrange
        Guid portId = Guid.NewGuid();
        using (AppDbContext context = _db.CreateContext())
        {
            context.Packets.Add(new PacketRecord
            {
                SourceBase = "K0OTH",
                SourceSsid = AprsSsid.AdditionalStation1,
                DestinationBase = "APRS",
                DestinationSsid = AprsSsid.PrimaryStation,
                Timestamp = DateTime.UtcNow.AddHours(-1),
                PortId = portId,
                PacketType = PacketTypes.Position,
                RawInfo = "!3745.50N/12159.50W-",
                IsOutbound = false,
                Position = new PositionDataRecord
                {
                    Location = new Coordinate(37.758333, -121.991667),
                    SymbolTable = SymbolTable.Primary,
                    SymbolCode = SymbolCode.HyphenMinus
                }
            });

            context.Packets.Add(new PacketRecord
            {
                SourceBase = "WX0TST",
                SourceSsid = AprsSsid.PrimaryStation,
                DestinationBase = "APRS",
                DestinationSsid = AprsSsid.PrimaryStation,
                Timestamp = DateTime.UtcNow.AddMinutes(-30),
                PortId = portId,
                PacketType = PacketTypes.Message,
                RawInfo = ":K0OTH-1  :Hello",
                IsOutbound = false,
                Message = new MessageDataRecord
                {
                    AddresseeBase = "K0OTH",
                    AddresseeSsid = AprsSsid.AdditionalStation1,
                    Text = "Hello",
                    Number = null
                }
            });

            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        using PacketQueryService service = new(_reader, _logger);

        // Act
        IReadOnlyDictionary<string, PacketRecord> result = await service.GetMostRecentPositionPacketsAsync(limit: 1000, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(result);
        Assert.Contains("K0OTH-1", result.Keys);
        Assert.DoesNotContain("WX0TST", result.Keys);
    }

    [Fact]
    public async Task GetPacketsByPortAsync_FiltersCorrectly()
    {
        // Arrange
        Guid port1 = Guid.NewGuid();
        Guid port2 = Guid.NewGuid();

        using (AppDbContext context = _db.CreateContext())
        {
            context.Packets.Add(new PacketRecord
            {
                SourceBase = "K0OTH",
                SourceSsid = AprsSsid.AdditionalStation1,
                DestinationBase = "APRS",
                DestinationSsid = AprsSsid.PrimaryStation,
                Timestamp = DateTime.UtcNow.AddHours(-1),
                PortId = port1,
                PacketType = PacketTypes.Position,
                RawInfo = "!3745.50N/12159.50W-",
                IsOutbound = false,
                Position = new PositionDataRecord
                {
                    Location = new Coordinate(37.758333, -121.991667),
                    SymbolTable = SymbolTable.Primary,
                    SymbolCode = SymbolCode.HyphenMinus
                }
            });

            context.Packets.Add(new PacketRecord
            {
                SourceBase = "WX0TST",
                SourceSsid = AprsSsid.PrimaryStation,
                DestinationBase = "APRS",
                DestinationSsid = AprsSsid.PrimaryStation,
                Timestamp = DateTime.UtcNow.AddMinutes(-30),
                PortId = port2,
                PacketType = PacketTypes.Position,
                RawInfo = "!3745.50N/12159.50W-",
                IsOutbound = false,
                Position = new PositionDataRecord
                {
                    Location = new Coordinate(37.758333, -121.991667),
                    SymbolTable = SymbolTable.Primary,
                    SymbolCode = SymbolCode.HyphenMinus
                }
            });

            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        using PacketQueryService service = new(_reader, _logger);

        // Act
        IReadOnlyList<PacketRecord> result = await service.GetPacketsByPortAsync(port1, limit: 500, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        PacketRecord item = Assert.Single(result);
        Assert.Equal("K0OTH", item.SourceBase);
        Assert.Equal(AprsSsid.AdditionalStation1, result[0].SourceSsid);
    }

    [Fact]
    public async Task GetPacketsByPortAsync_OrdersByIdDescending()
    {
        // Arrange
        Guid portId = Guid.NewGuid();
        DateTime baseTime = DateTime.UtcNow.AddHours(-10);
        using (AppDbContext context = _db.CreateContext())
        {
            // Create packets with deterministic timestamps that decrease
            context.Packets.Add(new PacketRecord
            {
                SourceBase = "K0OTH",
                SourceSsid = AprsSsid.AdditionalStation1,
                DestinationBase = "APRS",
                DestinationSsid = AprsSsid.PrimaryStation,
                Timestamp = baseTime,  // Oldest
                PortId = portId,
                PacketType = PacketTypes.Position,
                RawInfo = "!3745.50N/12159.50W-",
                IsOutbound = false,
                Position = new PositionDataRecord
                {
                    Location = new Coordinate(37.758333, -121.991667),
                    SymbolTable = SymbolTable.Primary,
                    SymbolCode = SymbolCode.HyphenMinus
                }
            });
            context.Packets.Add(new PacketRecord
            {
                SourceBase = "K0OTH",
                SourceSsid = AprsSsid.AdditionalStation1,
                DestinationBase = "APRS",
                DestinationSsid = AprsSsid.PrimaryStation,
                Timestamp = baseTime.AddHours(1),  // Middle
                PortId = portId,
                PacketType = PacketTypes.Position,
                RawInfo = "!3745.51N/12159.51W-",
                IsOutbound = false,
                Position = new PositionDataRecord
                {
                    Location = new Coordinate(37.759333, -121.992667),
                    SymbolTable = SymbolTable.Primary,
                    SymbolCode = SymbolCode.HyphenMinus
                }
            });
            context.Packets.Add(new PacketRecord
            {
                SourceBase = "K0OTH",
                SourceSsid = AprsSsid.AdditionalStation1,
                DestinationBase = "APRS",
                DestinationSsid = AprsSsid.PrimaryStation,
                Timestamp = baseTime.AddHours(2),  // Newest
                PortId = portId,
                PacketType = PacketTypes.Position,
                RawInfo = "!3745.52N/12159.52W-",
                IsOutbound = false,
                Position = new PositionDataRecord
                {
                    Location = new Coordinate(37.760333, -121.993667),
                    SymbolTable = SymbolTable.Primary,
                    SymbolCode = SymbolCode.HyphenMinus
                }
            });
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        using PacketQueryService service = new(_reader, _logger);

        // Act
        IReadOnlyList<PacketRecord> result = await service.GetPacketsByPortAsync(portId, limit: 500, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(3, result.Count);
        // Most recent first (highest ID, which corresponds to newest timestamp)
        Assert.True(result[0].Timestamp > result[1].Timestamp);
        Assert.True(result[1].Timestamp > result[2].Timestamp);
    }

    [Fact]
    public async Task GetPacketsByCallsignAsync_WithSsid_ReturnsMatchingPackets()
    {
        // Arrange
        Guid portId = Guid.NewGuid();
        using (AppDbContext context = _db.CreateContext())
        {
            context.Packets.Add(new PacketRecord
            {
                SourceBase = "K0OTH",
                SourceSsid = AprsSsid.AdditionalStation1,
                DestinationBase = "APRS",
                DestinationSsid = AprsSsid.PrimaryStation,
                Timestamp = DateTime.UtcNow.AddHours(-1),
                PortId = portId,
                PacketType = PacketTypes.Position,
                RawInfo = "!3745.50N/12159.50W-",
                IsOutbound = false,
                Position = new PositionDataRecord
                {
                    Location = new Coordinate(37.758333, -121.991667),
                    SymbolTable = SymbolTable.Primary,
                    SymbolCode = SymbolCode.HyphenMinus
                }
            });

            context.Packets.Add(new PacketRecord
            {
                SourceBase = "K0OTH",
                SourceSsid = AprsSsid.HumanPortable,
                DestinationBase = "APRS",
                DestinationSsid = AprsSsid.PrimaryStation,
                Timestamp = DateTime.UtcNow.AddMinutes(-30),
                PortId = portId,
                PacketType = PacketTypes.Position,
                RawInfo = "!3745.51N/12159.51W-",
                IsOutbound = false,
                Position = new PositionDataRecord
                {
                    Location = new Coordinate(37.758350, -121.991683),
                    SymbolTable = SymbolTable.Primary,
                    SymbolCode = SymbolCode.HyphenMinus
                }
            });

            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        using PacketQueryService service = new(_reader, _logger);

        // Act
        IReadOnlyList<PacketRecord> result = await service.GetPacketsByCallsignAsync("K0OTH-1", limit: 500, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        PacketRecord item = Assert.Single(result);
        Assert.Equal(AprsSsid.AdditionalStation1, item.SourceSsid);
    }

    [Fact]
    public async Task GetPacketsByCallsignAsync_WithoutSsid_ReturnsAllSsids()
    {
        // Arrange
        Guid portId = Guid.NewGuid();
        using (AppDbContext context = _db.CreateContext())
        {
            context.Packets.Add(new PacketRecord
            {
                SourceBase = "K0OTH",
                SourceSsid = AprsSsid.AdditionalStation1,
                DestinationBase = "APRS",
                DestinationSsid = AprsSsid.PrimaryStation,
                Timestamp = DateTime.UtcNow.AddHours(-1),
                PortId = portId,
                PacketType = PacketTypes.Position,
                RawInfo = "!3745.50N/12159.50W-",
                IsOutbound = false,
                Position = new PositionDataRecord
                {
                    Location = new Coordinate(37.758333, -121.991667),
                    SymbolTable = SymbolTable.Primary,
                    SymbolCode = SymbolCode.HyphenMinus
                }
            });

            context.Packets.Add(new PacketRecord
            {
                SourceBase = "K0OTH",
                SourceSsid = AprsSsid.HumanPortable,
                DestinationBase = "APRS",
                DestinationSsid = AprsSsid.PrimaryStation,
                Timestamp = DateTime.UtcNow.AddMinutes(-30),
                PortId = portId,
                PacketType = PacketTypes.Position,
                RawInfo = "!3745.51N/12159.51W-",
                IsOutbound = false,
                Position = new PositionDataRecord
                {
                    Location = new Coordinate(37.758350, -121.991683),
                    SymbolTable = SymbolTable.Primary,
                    SymbolCode = SymbolCode.HyphenMinus
                }
            });

            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        using PacketQueryService service = new(_reader, _logger);

        // Act
        IReadOnlyList<PacketRecord> result = await service.GetPacketsByCallsignAsync("K0OTH", limit: 500, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task GetPacketsByCallsignAsync_InvalidSsidFormat_FallsBackToBaseCallsign()
    {
        // Arrange
        Guid portId = Guid.NewGuid();
        using (AppDbContext context = _db.CreateContext())
        {
            context.Packets.Add(new PacketRecord
            {
                SourceBase = "K0OTH",
                SourceSsid = AprsSsid.AdditionalStation1,
                DestinationBase = "APRS",
                DestinationSsid = AprsSsid.PrimaryStation,
                Timestamp = DateTime.UtcNow.AddHours(-1),
                PortId = portId,
                PacketType = PacketTypes.Position,
                RawInfo = "!3745.50N/12159.50W-",
                IsOutbound = false,
                Position = new PositionDataRecord
                {
                    Location = new Coordinate(37.758333, -121.991667),
                    SymbolTable = SymbolTable.Primary,
                    SymbolCode = SymbolCode.HyphenMinus
                }
            });

            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        using PacketQueryService service = new(_reader, _logger);

        // Act
        IReadOnlyList<PacketRecord> result = await service.GetPacketsByCallsignAsync("K0OTH-99", limit: 500, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(result);
    }

    [Fact]
    public async Task GetPacketsByCallsignPageAsync_PagesNewestFirstWithAStableCursor()
    {
        // Arrange
        Guid portId = Guid.NewGuid();
        using (AppDbContext context = _db.CreateContext())
        {
            for (int index = 0; index < 3; index++)
            {
                context.Packets.Add(new PacketRecord
                {
                    SourceBase = "K0OTH",
                    SourceSsid = AprsSsid.AdditionalStation1,
                    DestinationBase = "APRS",
                    DestinationSsid = AprsSsid.PrimaryStation,
                    Timestamp = DateTime.UtcNow.AddMinutes(-index),
                    PortId = portId,
                    PacketType = PacketTypes.Position,
                    RawInfo = $"!3745.5{index}N/12159.5{index}W-",
                    IsOutbound = false,
                    Position = new PositionDataRecord
                    {
                        Location = new Coordinate(37.758333, -121.991667),
                        SymbolTable = SymbolTable.Primary,
                        SymbolCode = SymbolCode.HyphenMinus
                    }
                });
            }

            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        using PacketQueryService service = new(_reader, _logger);

        // Act
        IReadOnlyList<PacketRecord> first = await service.GetPacketsByCallsignPageAsync(
            "K0OTH-1", pageSize: 2, cancellationToken: TestContext.Current.CancellationToken);
        IReadOnlyList<PacketRecord> second = await service.GetPacketsByCallsignPageAsync(
            "K0OTH-1", cursor: PacketPageCursor.FromPacket(first[^1]), pageSize: 2, cancellationToken: TestContext.Current.CancellationToken);
        IReadOnlyList<PacketRecord> third = await service.GetPacketsByCallsignPageAsync(
            "K0OTH-1", cursor: PacketPageCursor.FromPacket(second[^1]), pageSize: 2, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, first.Count);
        PacketRecord remaining = Assert.Single(second);
        Assert.Empty(third);
        Assert.True(first[0].Timestamp > first[1].Timestamp);
        Assert.True(first[^1].Timestamp > remaining.Timestamp);
        Assert.Empty(first.Select(packet => packet.Id).Intersect(second.Select(packet => packet.Id)));
    }

    [Fact]
    public async Task GetPacketsByCallsignPageAsync_IdenticalTimestamps_NeitherSkipsNorRepeatsRows()
    {
        // Arrange - every row shares one receipt time, so only the id half of the
        // cursor can separate page boundaries.
        Guid portId = Guid.NewGuid();
        DateTime sharedTimestamp = new(2026, 11, 27, 10, 30, 0, DateTimeKind.Utc);
        using (AppDbContext context = _db.CreateContext())
        {
            for (int index = 0; index < 7; index++)
            {
                context.Packets.Add(new PacketRecord
                {
                    SourceBase = "K0OTH",
                    SourceSsid = AprsSsid.AdditionalStation1,
                    DestinationBase = "APRS",
                    DestinationSsid = AprsSsid.PrimaryStation,
                    Timestamp = sharedTimestamp,
                    PortId = portId,
                    PacketType = PacketTypes.Position,
                    RawInfo = $"!3745.5{index}N/12159.5{index}W-",
                    IsOutbound = false,
                    Position = new PositionDataRecord
                    {
                        Location = new Coordinate(37.758333, -121.991667),
                        SymbolTable = SymbolTable.Primary,
                        SymbolCode = SymbolCode.HyphenMinus
                    }
                });
            }

            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        using PacketQueryService service = new(_reader, _logger);

        // Act - walk the history three rows at a time.
        List<PacketRecord> visited = [];
        PacketPageCursor? cursor = null;
        while (true)
        {
            IReadOnlyList<PacketRecord> page = await service.GetPacketsByCallsignPageAsync(
                "K0OTH-1", cursor, pageSize: 3, cancellationToken: TestContext.Current.CancellationToken);
            if (page.Count == 0)
            {
                break;
            }

            visited.AddRange(page);
            cursor = PacketPageCursor.FromPacket(page[^1]);
        }

        // Assert - each row appears once, in id-descending order, with the page
        // boundaries landing inside the shared timestamp.
        Assert.Equal(7, visited.Count);
        Assert.Equal(7, visited.Select(packet => packet.Id).Distinct().Count());
        List<long> expectedOrder = [.. visited.Select(packet => packet.Id).OrderByDescending(id => id)];
        Assert.Equal(expectedOrder, [.. visited.Select(packet => packet.Id)]);
        Assert.All(visited, packet => Assert.Equal(sharedTimestamp, packet.Timestamp));
    }

    [Fact]
    public async Task GetPacketsByCallsignPageAsync_OutOfOrderReceiptTimes_UsesTimestampBeforeId()
    {
        // Arrange - a row inserted later can carry an older receipt time (imports,
        // backfill, clock correction). Ordering by id alone would order and page this
        // history wrongly, so the cursor must lead with the timestamp.
        Guid portId = Guid.NewGuid();
        DateTime tenAm = new(2026, 11, 27, 10, 0, 0, DateTimeKind.Utc);
        DateTime nineAm = tenAm.AddHours(-1);
        DateTime elevenAm = tenAm.AddHours(1);

        using (AppDbContext context = _db.CreateContext())
        {
            foreach ((string info, DateTime timestamp) in new[]
            {
                ("!3745.50N/12159.50W-", tenAm),
                ("!3745.51N/12159.51W-", nineAm),
                ("!3745.52N/12159.52W-", elevenAm)
            })
            {
                context.Packets.Add(new PacketRecord
                {
                    SourceBase = "K0OTH",
                    SourceSsid = AprsSsid.AdditionalStation1,
                    DestinationBase = "APRS",
                    DestinationSsid = AprsSsid.PrimaryStation,
                    Timestamp = timestamp,
                    PortId = portId,
                    PacketType = PacketTypes.Position,
                    RawInfo = info,
                    IsOutbound = false,
                    Position = new PositionDataRecord
                    {
                        Location = new Coordinate(37.758333, -121.991667),
                        SymbolTable = SymbolTable.Primary,
                        SymbolCode = SymbolCode.HyphenMinus
                    }
                });
            }

            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        using PacketQueryService service = new(_reader, _logger);

        // Act
        IReadOnlyList<PacketRecord> first = await service.GetPacketsByCallsignPageAsync(
            "K0OTH-1", pageSize: 1, cancellationToken: TestContext.Current.CancellationToken);
        IReadOnlyList<PacketRecord> second = await service.GetPacketsByCallsignPageAsync(
            "K0OTH-1", PacketPageCursor.FromPacket(first[^1]), pageSize: 1, cancellationToken: TestContext.Current.CancellationToken);
        IReadOnlyList<PacketRecord> third = await service.GetPacketsByCallsignPageAsync(
            "K0OTH-1", PacketPageCursor.FromPacket(second[^1]), pageSize: 1, cancellationToken: TestContext.Current.CancellationToken);

        // Assert - strictly by receipt time, then id.
        Assert.Equal(elevenAm, Assert.Single(first).Timestamp);
        Assert.Equal(tenAm, Assert.Single(second).Timestamp);
        Assert.Equal(nineAm, Assert.Single(third).Timestamp);
    }

    [Fact]
    public async Task GetPositionTrailPacketsAsync_NoCutoff_ReturnsAllPackets()
    {
        // Arrange
        Guid portId = Guid.NewGuid();
        using (AppDbContext context = _db.CreateContext())
        {
            for (int i = 0; i < 3; i++)
            {
                context.Packets.Add(new PacketRecord
                {
                    SourceBase = "K0OTH",
                    SourceSsid = AprsSsid.AdditionalStation1,
                    DestinationBase = "APRS",
                    DestinationSsid = AprsSsid.PrimaryStation,
                    Timestamp = DateTime.UtcNow.AddHours(-i),
                    PortId = portId,
                    PacketType = PacketTypes.Position,
                    RawInfo = $"!3745.5{i}N/12159.5{i}W-",
                    IsOutbound = false,
                    Position = new PositionDataRecord
                    {
                        Location = new Coordinate(37.758333 + i * 0.001, -121.991667 + i * 0.001),
                        SymbolTable = SymbolTable.Primary,
                        SymbolCode = SymbolCode.HyphenMinus
                    }
                });
            }
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        using PacketQueryService service = new(_reader, _logger);

        // Act
        IReadOnlyList<PacketRecord> result = await service.GetPositionTrailPacketsAsync(cutoffTime: null, limit: 50000, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(3, result.Count);
    }

    [Fact]
    public async Task GetPositionTrailPacketsAsync_WithCutoff_FiltersOldPackets()
    {
        // Arrange
        Guid portId = Guid.NewGuid();
        DateTime cutoff = DateTime.UtcNow.AddHours(-1.5);

        using (AppDbContext context = _db.CreateContext())
        {
            context.Packets.Add(new PacketRecord
            {
                SourceBase = "K0OTH",
                SourceSsid = AprsSsid.AdditionalStation1,
                DestinationBase = "APRS",
                DestinationSsid = AprsSsid.PrimaryStation,
                Timestamp = DateTime.UtcNow.AddHours(-2), // Before cutoff
                PortId = portId,
                PacketType = PacketTypes.Position,
                RawInfo = "!3745.50N/12159.50W-",
                IsOutbound = false,
                Position = new PositionDataRecord
                {
                    Location = new Coordinate(37.758333, -121.991667),
                    SymbolTable = SymbolTable.Primary,
                    SymbolCode = SymbolCode.HyphenMinus
                }
            });

            context.Packets.Add(new PacketRecord
            {
                SourceBase = "K0OTH",
                SourceSsid = AprsSsid.AdditionalStation1,
                DestinationBase = "APRS",
                DestinationSsid = AprsSsid.PrimaryStation,
                Timestamp = DateTime.UtcNow.AddHours(-1), // After cutoff
                PortId = portId,
                PacketType = PacketTypes.Position,
                RawInfo = "!3745.51N/12159.51W-",
                IsOutbound = false,
                Position = new PositionDataRecord
                {
                    Location = new Coordinate(37.758350, -121.991683),
                    SymbolTable = SymbolTable.Primary,
                    SymbolCode = SymbolCode.HyphenMinus
                }
            });

            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        using PacketQueryService service = new(_reader, _logger);

        // Act
        IReadOnlyList<PacketRecord> result = await service.GetPositionTrailPacketsAsync(cutoffTime: cutoff, limit: 50000, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(result);
    }

    [Fact]
    public async Task GetMostRecentObjectsAsync_ReturnsLatestPerObjectName()
    {
        // Arrange
        Guid portId = Guid.NewGuid();
        using (AppDbContext context = _db.CreateContext())
        {
            context.Packets.Add(new PacketRecord
            {
                SourceBase = "N0CALL",
                SourceSsid = AprsSsid.PrimaryStation,
                DestinationBase = "APRS",
                DestinationSsid = AprsSsid.PrimaryStation,
                Timestamp = DateTime.UtcNow.AddHours(-2),
                PortId = portId,
                PacketType = PacketTypes.Object,
                RawInfo = ";OBJ1     *111111z",
                IsOutbound = false,
                Object = new ObjectDataRecord
                {
                    Name = "OBJ1",
                    IsLive = true,
                    Location = new Coordinate(38.5, -9.0),
                    SymbolTable = SymbolTable.Primary,
                    SymbolCode = SymbolCode.HyphenMinus
                }
            });

            context.Packets.Add(new PacketRecord
            {
                SourceBase = "N0CALL",
                SourceSsid = AprsSsid.PrimaryStation,
                DestinationBase = "APRS",
                DestinationSsid = AprsSsid.PrimaryStation,
                Timestamp = DateTime.UtcNow.AddHours(-1),
                PortId = portId,
                PacketType = PacketTypes.Object,
                RawInfo = ";OBJ1     _111111z",
                IsOutbound = false,
                Object = new ObjectDataRecord
                {
                    Name = "OBJ1",
                    IsLive = false,
                    Location = new Coordinate(38.5, -9.0),
                    SymbolTable = SymbolTable.Primary,
                    SymbolCode = SymbolCode.HyphenMinus
                }
            });

            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        using PacketQueryService service = new(_reader, _logger);

        // Act
        IReadOnlyList<PacketRecord> result = await service.GetMostRecentObjectsAsync(limit: 1000, cancellationToken: TestContext.Current.CancellationToken);

        // Assert - only the latest (killed) object survives per name
        PacketRecord item = Assert.Single(result);
        Assert.False(item.Object?.IsLive);
    }

    [Fact]
    public async Task GetPacketsByObjectNamePageAsync_PagesReportsNewestFirst()
    {
        // Arrange
        Guid portId = Guid.NewGuid();
        using (AppDbContext context = _db.CreateContext())
        {
            for (int index = 0; index < 3; index++)
            {
                context.Packets.Add(new PacketRecord
                {
                    SourceBase = "N0CALL",
                    SourceSsid = AprsSsid.PrimaryStation,
                    DestinationBase = "APRS",
                    DestinationSsid = AprsSsid.PrimaryStation,
                    Timestamp = DateTime.UtcNow.AddMinutes(-index),
                    PortId = portId,
                    PacketType = PacketTypes.Object,
                    RawInfo = $";OBJ1     *11111{index}z",
                    IsOutbound = false,
                    Object = new ObjectDataRecord
                    {
                        Name = "OBJ1",
                        IsLive = true,
                        Location = new Coordinate(38.5, -9.0),
                        SymbolTable = SymbolTable.Primary,
                        SymbolCode = SymbolCode.HyphenMinus
                    }
                });
            }

            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        using PacketQueryService service = new(_reader, _logger);

        // Act
        IReadOnlyList<PacketRecord> first = await service.GetPacketsByObjectNamePageAsync(
            "OBJ1", pageSize: 2, cancellationToken: TestContext.Current.CancellationToken);
        IReadOnlyList<PacketRecord> second = await service.GetPacketsByObjectNamePageAsync(
            "OBJ1", cursor: PacketPageCursor.FromPacket(first[^1]), pageSize: 2, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, first.Count);
        PacketRecord remaining = Assert.Single(second);
        Assert.True(first[0].Timestamp > first[1].Timestamp);
        Assert.True(first[^1].Timestamp > remaining.Timestamp);
    }

    [Fact]
    public async Task GetPacketsByObjectNamePageAsync_IdenticalTimestamps_NeitherSkipsNorRepeatsReports()
    {
        // Arrange
        Guid portId = Guid.NewGuid();
        DateTime sharedTimestamp = new(2026, 11, 27, 8, 0, 0, DateTimeKind.Utc);
        using (AppDbContext context = _db.CreateContext())
        {
            for (int index = 0; index < 5; index++)
            {
                context.Packets.Add(new PacketRecord
                {
                    SourceBase = "N0CALL",
                    SourceSsid = AprsSsid.PrimaryStation,
                    DestinationBase = "APRS",
                    DestinationSsid = AprsSsid.PrimaryStation,
                    Timestamp = sharedTimestamp,
                    PortId = portId,
                    PacketType = PacketTypes.Object,
                    RawInfo = $";OBJ1     *11111{index}z",
                    IsOutbound = false,
                    Object = new ObjectDataRecord
                    {
                        Name = "OBJ1",
                        IsLive = true,
                        Location = new Coordinate(38.5, -9.0),
                        SymbolTable = SymbolTable.Primary,
                        SymbolCode = SymbolCode.HyphenMinus
                    }
                });
            }

            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        using PacketQueryService service = new(_reader, _logger);

        // Act
        List<PacketRecord> visited = [];
        PacketPageCursor? cursor = null;
        while (true)
        {
            IReadOnlyList<PacketRecord> page = await service.GetPacketsByObjectNamePageAsync(
                "OBJ1", cursor, pageSize: 2, cancellationToken: TestContext.Current.CancellationToken);
            if (page.Count == 0)
            {
                break;
            }

            visited.AddRange(page);
            cursor = PacketPageCursor.FromPacket(page[^1]);
        }

        // Assert
        Assert.Equal(5, visited.Count);
        Assert.Equal(5, visited.Select(packet => packet.Id).Distinct().Count());
    }

    [Fact]
    public async Task GetMostRecentItemsAsync_ReturnsLatestPerItemName()
    {
        // Arrange
        Guid portId = Guid.NewGuid();
        using (AppDbContext context = _db.CreateContext())
        {
            context.Packets.Add(new PacketRecord
            {
                SourceBase = "N0CALL",
                SourceSsid = AprsSsid.PrimaryStation,
                DestinationBase = "APRS",
                DestinationSsid = AprsSsid.PrimaryStation,
                Timestamp = DateTime.UtcNow.AddHours(-1),
                PortId = portId,
                PacketType = PacketTypes.Item,
                RawInfo = ")ITEM!",
                IsOutbound = false,
                Item = new ItemDataRecord
                {
                    Name = "ITEM",
                    IsLive = true,
                    Location = new Coordinate(38.5, -9.0),
                    SymbolTable = SymbolTable.Alternate,
                    SymbolCode = SymbolCode.HyphenMinus
                }
            });

            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        using PacketQueryService service = new(_reader, _logger);

        // Act
        IReadOnlyList<PacketRecord> result = await service.GetMostRecentItemsAsync(limit: 1000, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        PacketRecord item = Assert.Single(result);
        Assert.Equal("ITEM", item.Item?.Name);
    }

    [Fact]
    public async Task GetBulletinsAsync_ReturnsNewestFirst()
    {
        // Arrange
        Guid portId = Guid.NewGuid();
        using (AppDbContext context = _db.CreateContext())
        {
            for (int i = 0; i < 3; i++)
            {
                context.Packets.Add(new PacketRecord
                {
                    SourceBase = "N0CALL",
                    SourceSsid = AprsSsid.PrimaryStation,
                    DestinationBase = "APRS",
                    DestinationSsid = AprsSsid.PrimaryStation,
                    Timestamp = DateTime.UtcNow.AddHours(-i),
                    PortId = portId,
                    PacketType = PacketTypes.Bulletin,
                    RawInfo = $":BLN0SPCL  :bulletin {i}",
                    IsOutbound = false,
                    Bulletin = new BulletinDataRecord
                    {
                        Name = "BLN0SPCL",
                        Text = $"bulletin {i}",
                        Number = i
                    }
                });
            }

            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        using PacketQueryService service = new(_reader, _logger);

        // Act
        IReadOnlyList<PacketRecord> result = await service.GetBulletinsAsync(limit: 500, cancellationToken: TestContext.Current.CancellationToken);

        // Assert - newest first (descending ID)
        Assert.Equal(3, result.Count);
        Assert.Equal(2, result[0].Bulletin?.Number);
        Assert.Equal(1, result[1].Bulletin?.Number);
        Assert.Equal(0, result[2].Bulletin?.Number);
    }

    [Fact]
    public async Task Dispose_CanBeCalledMultipleTimes()
    {
        // Arrange
        PacketQueryService service = new(_reader, _logger);

        // Act
        service.Dispose();
        service.Dispose();

        // Assert - no exception
    }

    [Fact]
    public async Task MethodsAfterDispose_ThrowObjectDisposedException()
    {
        // Arrange
        PacketQueryService service = new(_reader, _logger);
        service.Dispose();

        // Act & Assert
        await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
            await service.GetMostRecentPacketsAsync(limit: 1000, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SearchStationsAsync_EmptyQuery_ReturnsEmptyList()
    {
        // Arrange
        using PacketQueryService service = new(_reader, _logger);

        // Act
        IReadOnlyList<PacketRecord> result = await service.SearchStationsAsync("   ", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public async Task SearchStationsAsync_ExactCallsignMatch_ReturnsStation()
    {
        // Arrange
        Guid portId = Guid.NewGuid();
        using (AppDbContext context = _db.CreateContext())
        {
            context.Packets.Add(new PacketRecord
            {
                SourceBase = "K0OTH",
                SourceSsid = AprsSsid.AdditionalStation1,
                DestinationBase = "APRS",
                DestinationSsid = AprsSsid.PrimaryStation,
                Timestamp = DateTime.UtcNow.AddMinutes(-30),
                PortId = portId,
                PacketType = PacketTypes.Position,
                RawInfo = "!3745.50N/12159.50W-",
                IsOutbound = false,
                Position = new PositionDataRecord
                {
                    Location = new Coordinate(37.758333, -121.991667),
                    SymbolTable = SymbolTable.Primary,
                    SymbolCode = SymbolCode.HyphenMinus
                }
            });

            context.Packets.Add(new PacketRecord
            {
                SourceBase = "WX0TST",
                SourceSsid = AprsSsid.PrimaryStation,
                DestinationBase = "APRS",
                DestinationSsid = AprsSsid.PrimaryStation,
                Timestamp = DateTime.UtcNow.AddMinutes(-10),
                PortId = portId,
                PacketType = PacketTypes.Position,
                RawInfo = "!3745.50N/12159.50W-",
                IsOutbound = false,
                Position = new PositionDataRecord
                {
                    Location = new Coordinate(37.758333, -121.991667),
                    SymbolTable = SymbolTable.Primary,
                    SymbolCode = SymbolCode.HyphenMinus
                }
            });

            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        using PacketQueryService service = new(_reader, _logger);

        // Act
        IReadOnlyList<PacketRecord> result = await service.SearchStationsAsync("K0OTH", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        PacketRecord station = Assert.Single(result);
        Assert.Equal("K0OTH", station.SourceBase);
    }

    [Fact]
    public async Task SearchStationsAsync_PrefixMatch_ReturnsMatchingStations()
    {
        // Arrange
        Guid portId = Guid.NewGuid();
        using (AppDbContext context = _db.CreateContext())
        {
            context.Packets.Add(new PacketRecord
            {
                SourceBase = "K0OTH",
                SourceSsid = AprsSsid.PrimaryStation,
                DestinationBase = "APRS",
                DestinationSsid = AprsSsid.PrimaryStation,
                Timestamp = DateTime.UtcNow.AddMinutes(-30),
                PortId = portId,
                PacketType = PacketTypes.Position,
                RawInfo = "!3745.50N/12159.50W-",
                IsOutbound = false,
                Position = new PositionDataRecord
                {
                    Location = new Coordinate(37.758333, -121.991667),
                    SymbolTable = SymbolTable.Primary,
                    SymbolCode = SymbolCode.HyphenMinus
                }
            });

            context.Packets.Add(new PacketRecord
            {
                SourceBase = "K0ABC",
                SourceSsid = AprsSsid.PrimaryStation,
                DestinationBase = "APRS",
                DestinationSsid = AprsSsid.PrimaryStation,
                Timestamp = DateTime.UtcNow.AddMinutes(-10),
                PortId = portId,
                PacketType = PacketTypes.Position,
                RawInfo = "!3745.50N/12159.50W-",
                IsOutbound = false,
                Position = new PositionDataRecord
                {
                    Location = new Coordinate(37.758333, -121.991667),
                    SymbolTable = SymbolTable.Primary,
                    SymbolCode = SymbolCode.HyphenMinus
                }
            });

            context.Packets.Add(new PacketRecord
            {
                SourceBase = "WX0TST",
                SourceSsid = AprsSsid.PrimaryStation,
                DestinationBase = "APRS",
                DestinationSsid = AprsSsid.PrimaryStation,
                Timestamp = DateTime.UtcNow.AddMinutes(-5),
                PortId = portId,
                PacketType = PacketTypes.Position,
                RawInfo = "!3745.50N/12159.50W-",
                IsOutbound = false,
                Position = new PositionDataRecord
                {
                    Location = new Coordinate(37.758333, -121.991667),
                    SymbolTable = SymbolTable.Primary,
                    SymbolCode = SymbolCode.HyphenMinus
                }
            });

            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        using PacketQueryService service = new(_reader, _logger);

        // Act
        IReadOnlyList<PacketRecord> result = await service.SearchStationsAsync("K0", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, result.Count);
        Assert.All(result, r => Assert.StartsWith("K0", r.SourceBase));
    }

    [Fact]
    public async Task SearchStationsAsync_ObjectNameMatch_ReturnsObject()
    {
        // Arrange
        Guid portId = Guid.NewGuid();
        using (AppDbContext context = _db.CreateContext())
        {
            context.Packets.Add(new PacketRecord
            {
                SourceBase = "N0CALL",
                SourceSsid = AprsSsid.PrimaryStation,
                DestinationBase = "APRS",
                DestinationSsid = AprsSsid.PrimaryStation,
                Timestamp = DateTime.UtcNow.AddMinutes(-10),
                PortId = portId,
                PacketType = PacketTypes.Object,
                RawInfo = ";SHELTER1 *111111z",
                IsOutbound = false,
                Object = new ObjectDataRecord
                {
                    Name = "SHELTER1",
                    IsLive = true,
                    Location = new Coordinate(38.5, -9.0),
                    SymbolTable = SymbolTable.Primary,
                    SymbolCode = SymbolCode.HyphenMinus
                }
            });

            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        using PacketQueryService service = new(_reader, _logger);

        // Act
        IReadOnlyList<PacketRecord> result = await service.SearchStationsAsync("SHEL", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        PacketRecord obj = Assert.Single(result);
        Assert.Equal("SHELTER1", obj.Object?.Name);
    }

    [Fact]
    public async Task SearchStationsAsync_CaseInsensitive_MatchesUppercaseCallsign()
    {
        // Arrange
        Guid portId = Guid.NewGuid();
        using (AppDbContext context = _db.CreateContext())
        {
            context.Packets.Add(new PacketRecord
            {
                SourceBase = "K0OTH",
                SourceSsid = AprsSsid.PrimaryStation,
                DestinationBase = "APRS",
                DestinationSsid = AprsSsid.PrimaryStation,
                Timestamp = DateTime.UtcNow.AddMinutes(-10),
                PortId = portId,
                PacketType = PacketTypes.Position,
                RawInfo = "!3745.50N/12159.50W-",
                IsOutbound = false,
                Position = new PositionDataRecord
                {
                    Location = new Coordinate(37.758333, -121.991667),
                    SymbolTable = SymbolTable.Primary,
                    SymbolCode = SymbolCode.HyphenMinus
                }
            });

            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        using PacketQueryService service = new(_reader, _logger);

        // Act
        IReadOnlyList<PacketRecord> result = await service.SearchStationsAsync("k0oth", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        PacketRecord station = Assert.Single(result);
        Assert.Equal("K0OTH", station.SourceBase);
    }

    [Fact]
    public async Task SearchStationsAsync_MultiplePacketsSameStation_ReturnsOnlyMostRecent()
    {
        // Arrange
        Guid portId = Guid.NewGuid();
        using (AppDbContext context = _db.CreateContext())
        {
            context.Packets.Add(new PacketRecord
            {
                SourceBase = "K0OTH",
                SourceSsid = AprsSsid.PrimaryStation,
                DestinationBase = "APRS",
                DestinationSsid = AprsSsid.PrimaryStation,
                Timestamp = DateTime.UtcNow.AddHours(-2),
                PortId = portId,
                PacketType = PacketTypes.Position,
                RawInfo = "!3745.50N/12159.50W-",
                IsOutbound = false,
                Position = new PositionDataRecord
                {
                    Location = new Coordinate(37.758333, -121.991667),
                    SymbolTable = SymbolTable.Primary,
                    SymbolCode = SymbolCode.HyphenMinus
                }
            });

            context.Packets.Add(new PacketRecord
            {
                SourceBase = "K0OTH",
                SourceSsid = AprsSsid.PrimaryStation,
                DestinationBase = "APRS",
                DestinationSsid = AprsSsid.PrimaryStation,
                Timestamp = DateTime.UtcNow.AddHours(-1),
                PortId = portId,
                PacketType = PacketTypes.Status,
                RawInfo = ">status update",
                IsOutbound = false,
                Status = new StatusDataRecord { Text = "status update" }
            });

            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        using PacketQueryService service = new(_reader, _logger);

        // Act
        IReadOnlyList<PacketRecord> result = await service.SearchStationsAsync("K0OTH", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        PacketRecord station = Assert.Single(result);
        Assert.Equal(PacketTypes.Status, station.PacketType);
    }
}
