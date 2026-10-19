// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using AetherAprs.Data.Entities;
using AetherAprs.Data.Mappers;
using AetherAprs.Models.Aprs;
using AetherAprs.Models.Aprs.Packets;
using Geo;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace AetherAprs.Tests.Data.Mappers;

public sealed class PacketRecordMapperTests
{
    private static PacketRecord CreateRecord(string packetType)
    {
        return new PacketRecord
        {
            SourceBase = "N0CALL",
            SourceSsid = AprsSsid.PrimaryStation,
            DestinationBase = "APRS",
            DestinationSsid = AprsSsid.PrimaryStation,
            PacketType = packetType,
            Timestamp = DateTime.UtcNow,
            PacketTimestamp = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc),
            PortId = Guid.NewGuid(),
            IsOutbound = false,
            RawInfo = "raw"
        };
    }

    [Fact]
    public void MapToPacket_PositionRecord_ReturnsPositionPacket()
    {
        var record = CreateRecord("Position");
        record.Position = new PositionDataRecord
        {
            Location = new Coordinate(38.5, -9.0),
            Altitude = 328.084,
            Course = 90,
            Speed = 10,
            SymbolTable = SymbolTable.Primary,
            SymbolCode = SymbolCode.HyphenMinus,
            Comment = "comment"
        };

        var packet = PacketRecordMapper.MapToPacket(record);

        var pos = Assert.IsType<PositionPacket>(packet);
        // Mapper always passes an explicit SSID (PrimaryStation when null), so compare base + ssid
        Assert.Equal("N0CALL", pos.Source.Base);
        Assert.Equal("APRS", pos.Destination.Base);
        Assert.Equal(38.5, pos.Location.Latitude);
        Assert.Equal(-9.0, pos.Location.Longitude);
        Assert.Equal(328.084, pos.Altitude);
        Assert.Equal(90, pos.Course);
        Assert.Equal(10, pos.Speed);
        Assert.Equal(new Symbol(SymbolTable.Primary, SymbolCode.HyphenMinus), pos.Symbol);
        Assert.Equal("comment", pos.Comment);
        Assert.Equal("raw", pos.Raw);
        Assert.NotNull(pos.Timestamp);
    }

    [Fact]
    public void MapToPacket_MessageRecord_ReturnsMessagePacket()
    {
        var record = CreateRecord("Message");
        record.Message = new MessageDataRecord
        {
            AddresseeBase = "K0OTH",
            AddresseeSsid = AprsSsid.HumanPortable,
            Text = "Hello",
            Number = 5
        };

        var packet = PacketRecordMapper.MapToPacket(record);

        var msg = Assert.IsType<MessagePacket>(packet);
        Assert.Equal(new Callsign("K0OTH", 7), msg.Addressee);        Assert.Equal("Hello", msg.Text);
        Assert.Equal(5, msg.MessageNumber);
    }

    [Fact]
    public void MapToPacket_StatusRecord_ReturnsStatusPacket()
    {
        var record = CreateRecord("Status");
        record.Status = new StatusDataRecord { Text = "on RF" };

        var packet = PacketRecordMapper.MapToPacket(record);

        var status = Assert.IsType<StatusPacket>(packet);
        Assert.Equal("on RF", status.Text);
    }

    [Fact]
    public void MapToPacket_WeatherRecord_ReturnsWeatherPacket()
    {
        var record = CreateRecord("Weather");
        record.Weather = new WeatherDataRecord
        {
            Temperature = 21.5,
            WindSpeed = 3,
            WindDirection = 270,
            Humidity = 55,
            Pressure = 1013,
            RainLastHour = 1,
            RainLast24Hours = 4
        };

        var packet = PacketRecordMapper.MapToPacket(record);

        var weather = Assert.IsType<WeatherPacket>(packet);
        Assert.Equal(21.5, weather.Temperature);
        Assert.Equal(3, weather.WindSpeed);
        Assert.Equal(270, weather.WindDirection);
        Assert.Equal(55, weather.Humidity);
        Assert.Equal(1013, weather.Pressure);
        Assert.Equal(1, weather.Rain1h);
        Assert.Equal(4, weather.Rain24h);
    }

    [Theory]
    [InlineData("Unknown")]
    [InlineData("MessageAck")]
    [InlineData("MessageRej")]
    [InlineData("Telemetry")]
    [InlineData("Capabilities")]
    [InlineData("Bogus")]
    public void MapToPacket_UnmappedPacketType_ReturnsUnknownPacket(string packetType)
    {
        var record = CreateRecord(packetType);

        var packet = PacketRecordMapper.MapToPacket(record);

        Assert.IsType<UnknownPacket>(packet);
    }

    [Fact]
    public void MapToPacket_PositionTypeWithMissingOwnedData_ReturnsUnknownPacket()
    {
        var record = CreateRecord("Position");
        record.Position = null;

        var packet = PacketRecordMapper.MapToPacket(record);

        Assert.IsType<UnknownPacket>(packet);
    }

    [Fact]
    public void MapToPacket_InvalidSourceBase_ReturnsNullAndLogsWarning()
    {
        var record = CreateRecord("Status");
        record.SourceBase = "BAD!";
        record.Status = new StatusDataRecord { Text = "x" };
        var logger = Substitute.For<ILogger>();

        var packet = PacketRecordMapper.MapToPacket(record, logger);

        Assert.Null(packet);
        logger.Received().Log(
            LogLevel.Warning,
            Arg.Any<EventId>(),
            Arg.Any<object>(),
            Arg.Any<Exception>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Fact]
    public void MapToPacket_PacketTimestamp_ReadsAsUtc()
    {
        var record = CreateRecord("Status");
        record.Status = new StatusDataRecord { Text = "x" };

        var packet = PacketRecordMapper.MapToPacket(record);

        var status = Assert.IsType<StatusPacket>(packet);
        Assert.Equal(new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero), status.Timestamp);
    }

    [Fact]
    public void MapToPacket_NoPacketTimestamp_TimestampIsNull()
    {
        var record = CreateRecord("Status");
        record.PacketTimestamp = null;
        record.Status = new StatusDataRecord { Text = "x" };

        var packet = PacketRecordMapper.MapToPacket(record);

        var status = Assert.IsType<StatusPacket>(packet);
        Assert.Null(status.Timestamp);
    }

    [Fact]
    public void MapToPacket_NullLogger_OnParseFailure_ReturnsNullWithoutThrowing()
    {
        var record = CreateRecord("Status");
        record.SourceBase = "BAD!";

        var packet = PacketRecordMapper.MapToPacket(record, NullLogger.Instance);

        Assert.Null(packet);
    }
}
