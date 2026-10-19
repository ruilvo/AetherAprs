// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Linq;
using System.Threading.Tasks;
using AetherAprs.Data;
using AetherAprs.Data.Entities;
using AetherAprs.Data.Mappers;
using AetherAprs.Models.Aprs;
using AetherAprs.Services.Packets;
using AetherAprs.Tests.Helpers;
using Geo;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace AetherAprs.Tests.Services.Packets;

public sealed class PacketQueryServiceTests : IDisposable
{
    private readonly TempAppDatabase _db;
    private readonly ILogger<PacketQueryService> _logger;

    public PacketQueryServiceTests()
    {
        _db = TempAppDatabase.CreateEmpty();
        using (var db = _db.CreateContext())
        {
            db.Database.EnsureCreated();
        }
        _logger = Substitute.For<ILogger<PacketQueryService>>();
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    [Fact]
    public async Task GetMostRecentPacketsAsync_EmptyDatabase_ReturnsEmptyDictionary()
    {
        // Arrange
        using var service = new PacketQueryService(_db.Factory, _logger);

        // Act
        var result = await service.GetMostRecentPacketsAsync(limit: 1000, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetMostRecentPacketsAsync_MultiplePacketsFromSameStation_ReturnsOnlyMostRecent()
    {
        // Arrange
        var portId = Guid.NewGuid();
        using (var context = _db.CreateContext())
        {
            var oldTimestamp = DateTime.UtcNow.AddHours(-2);
            var recentTimestamp = DateTime.UtcNow.AddHours(-1);

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

        using var service = new PacketQueryService(_db.Factory, _logger);

        // Act
        var result = await service.GetMostRecentPacketsAsync(limit: 1000, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(result);
        var packet = result["K0OTH-1"];
        Assert.Equal("Recent position", packet.Position?.Comment);
    }

    [Fact]
    public async Task GetMostRecentPacketsAsync_MultipleStations_ReturnsOnePerStation()
    {
        // Arrange
        var portId = Guid.NewGuid();
        using (var context = _db.CreateContext())
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

        using var service = new PacketQueryService(_db.Factory, _logger);

        // Act
        var result = await service.GetMostRecentPacketsAsync(limit: 1000, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Contains("K0OTH-1", result.Keys);
        Assert.Contains("WX0TST", result.Keys);
    }

    [Fact]
    public async Task GetMostRecentPacketsAsync_EnforcesLimitParameter()
    {
        // Arrange
        var portId = Guid.NewGuid();
        using (var context = _db.CreateContext())
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

        using var service = new PacketQueryService(_db.Factory, _logger);

        // Act
        var result = await service.GetMostRecentPacketsAsync(limit: 3, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(3, result.Count);
    }

    [Fact]
    public async Task GetMostRecentPositionPacketsAsync_FiltersNonPositionPackets()
    {
        // Arrange
        var portId = Guid.NewGuid();
        using (var context = _db.CreateContext())
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

        using var service = new PacketQueryService(_db.Factory, _logger);

        // Act
        var result = await service.GetMostRecentPositionPacketsAsync(limit: 1000, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(result);
        Assert.Contains("K0OTH-1", result.Keys);
        Assert.DoesNotContain("WX0TST", result.Keys);
    }

    [Fact]
    public async Task GetPacketsByPortAsync_FiltersCorrectly()
    {
        // Arrange
        var port1 = Guid.NewGuid();
        var port2 = Guid.NewGuid();

        using (var context = _db.CreateContext())
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

        using var service = new PacketQueryService(_db.Factory, _logger);

        // Act
        var result = await service.GetPacketsByPortAsync(port1, limit: 500, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(result);
        Assert.Equal("K0OTH", result[0].SourceBase);
        Assert.Equal(AprsSsid.AdditionalStation1, result[0].SourceSsid);
    }

    [Fact]
    public async Task GetPacketsByPortAsync_OrdersByIdDescending()
    {
        // Arrange
        var portId = Guid.NewGuid();
        var baseTime = DateTime.UtcNow.AddHours(-10);
        using (var context = _db.CreateContext())
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

        using var service = new PacketQueryService(_db.Factory, _logger);

        // Act
        var result = await service.GetPacketsByPortAsync(portId, limit: 500, cancellationToken: TestContext.Current.CancellationToken);

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
        var portId = Guid.NewGuid();
        using (var context = _db.CreateContext())
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

        using var service = new PacketQueryService(_db.Factory, _logger);

        // Act
        var result = await service.GetPacketsByCallsignAsync("K0OTH-1", limit: 500, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(result);
        Assert.Equal(AprsSsid.AdditionalStation1, result[0].SourceSsid);
    }

    [Fact]
    public async Task GetPacketsByCallsignAsync_WithoutSsid_ReturnsAllSsids()
    {
        // Arrange
        var portId = Guid.NewGuid();
        using (var context = _db.CreateContext())
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

        using var service = new PacketQueryService(_db.Factory, _logger);

        // Act
        var result = await service.GetPacketsByCallsignAsync("K0OTH", limit: 500, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task GetPacketsByCallsignAsync_InvalidSsidFormat_FallsBackToBaseCallsign()
    {
        // Arrange
        var portId = Guid.NewGuid();
        using (var context = _db.CreateContext())
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

        using var service = new PacketQueryService(_db.Factory, _logger);

        // Act
        var result = await service.GetPacketsByCallsignAsync("K0OTH-99", limit: 500, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(result);
    }

    [Fact]
    public async Task GetPositionTrailPacketsAsync_NoCutoff_ReturnsAllPackets()
    {
        // Arrange
        var portId = Guid.NewGuid();
        using (var context = _db.CreateContext())
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

        using var service = new PacketQueryService(_db.Factory, _logger);

        // Act
        var result = await service.GetPositionTrailPacketsAsync(cutoffTime: null, limit: 50000, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(3, result.Count);
    }

    [Fact]
    public async Task GetPositionTrailPacketsAsync_WithCutoff_FiltersOldPackets()
    {
        // Arrange
        var portId = Guid.NewGuid();
        var cutoff = DateTime.UtcNow.AddHours(-1.5);

        using (var context = _db.CreateContext())
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

        using var service = new PacketQueryService(_db.Factory, _logger);

        // Act
        var result = await service.GetPositionTrailPacketsAsync(cutoffTime: cutoff, limit: 50000, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(result);
    }

    [Fact]
    public async Task Dispose_CanBeCalledMultipleTimes()
    {
        // Arrange
        var service = new PacketQueryService(_db.Factory, _logger);

        // Act
        service.Dispose();
        service.Dispose();

        // Assert - no exception
    }

    [Fact]
    public async Task MethodsAfterDispose_ThrowObjectDisposedException()
    {
        // Arrange
        var service = new PacketQueryService(_db.Factory, _logger);
        service.Dispose();

        // Act & Assert
        await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
            await service.GetMostRecentPacketsAsync(limit: 1000, cancellationToken: TestContext.Current.CancellationToken));
    }
}
