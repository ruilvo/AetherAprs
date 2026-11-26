// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using AetherAprs.Data.Entities;
using AetherAprs.Data.Mappers;
using AetherAprs.Models.Aprs;
using AetherAprs.Models.Aprs.Packets;
using AetherAprs.Tests.Helpers;
using Geo;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace AetherAprs.Tests.Data.Mappers;

public sealed class PacketRecordMapperTests
{
    private static PacketRecord CreateRecord(string packetType) => new()
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

    [Fact]
    public void MapToPacket_PositionRecord_ReturnsPositionPacket()
    {
        PacketRecord record = CreateRecord("Position");
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

        AprsPacket? packet = PacketRecordMapper.MapToPacket(record);

        PositionPacket pos = Assert.IsType<PositionPacket>(packet);
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
        PacketRecord record = CreateRecord("Message");
        record.Message = new MessageDataRecord
        {
            AddresseeBase = "K0OTH",
            AddresseeSsid = AprsSsid.HumanPortable,
            Text = "Hello",
            Number = 5
        };

        AprsPacket? packet = PacketRecordMapper.MapToPacket(record);

        MessagePacket msg = Assert.IsType<MessagePacket>(packet);
        Assert.Equal(AprsAddress.FromCallsign(new Callsign("K0OTH", 7)), msg.Addressee); Assert.Equal("Hello", msg.Text);
        Assert.Equal(5, msg.MessageNumber);
    }

    [Fact]
    public void MapToPacket_MessageStoredWithPrimaryStationSsid_ReadsAsNoSsid()
    {
        PacketRecord record = CreateRecord("Message");
        record.Message = new MessageDataRecord
        {
            AddresseeBase = "APRS2SOTA",
            AddresseeSsid = AprsSsid.PrimaryStation,
            Text = "Summit",
            Number = 1
        };

        AprsPacket? packet = PacketRecordMapper.MapToPacket(record);

        MessagePacket msg = Assert.IsType<MessagePacket>(packet);
        Assert.Equal("APRS2SOTA", msg.Addressee.Base);
        Assert.Null(msg.Addressee.Ssid);
        Assert.Equal(new AprsAddress("APRS2SOTA"), msg.Addressee);
        Assert.NotEqual(new AprsAddress("APRS2SOTA", 0), msg.Addressee);
    }

    [Fact]
    public void ToRecord_ThenMapToPacket_KeepsATacticalAddresseeWithoutAnSsid()
    {
        MessagePacket packet = new()
        {
            Source = new Callsign("N0CALL"),
            Destination = new Callsign("APRS"),
            Addressee = new AprsAddress("APRS2SOTA"),
            Text = "SOTA spot",
            MessageNumber = 1
        };

        PacketRecord record = PacketRecordMapper.ToRecord(
            packet, Guid.NewGuid(), DateTimeOffset.UtcNow, isOutbound: true);
        AprsPacket? reloaded = PacketRecordMapper.MapToPacket(record);

        MessagePacket message = Assert.IsType<MessagePacket>(reloaded);
        Assert.True(AprsAddress.TryParse("APRS2SOTA", out AprsAddress expected));
        Assert.Equal(expected, message.Addressee);
    }

    [Fact]
    public void MapToPacket_StatusRecord_ReturnsStatusPacket()
    {
        PacketRecord record = CreateRecord("Status");
        record.Status = new StatusDataRecord { Text = "on RF" };

        AprsPacket? packet = PacketRecordMapper.MapToPacket(record);

        StatusPacket status = Assert.IsType<StatusPacket>(packet);
        Assert.Equal("on RF", status.Text);
    }

    [Fact]
    public void MapToPacket_WeatherRecord_ReturnsWeatherPacket()
    {
        PacketRecord record = CreateRecord("Weather");
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

        AprsPacket? packet = PacketRecordMapper.MapToPacket(record);

        WeatherPacket weather = Assert.IsType<WeatherPacket>(packet);
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
    [InlineData("Bogus")]
    public void MapToPacket_UnmappedPacketType_ReturnsUnknownPacket(string packetType)
    {
        PacketRecord record = CreateRecord(packetType);

        AprsPacket? packet = PacketRecordMapper.MapToPacket(record);

        Assert.IsType<UnknownPacket>(packet);
    }

    [Fact]
    public void MapToPacket_PositionTypeWithMissingOwnedData_ReturnsUnknownPacket()
    {
        PacketRecord record = CreateRecord("Position");
        record.Position = null;

        AprsPacket? packet = PacketRecordMapper.MapToPacket(record);

        Assert.IsType<UnknownPacket>(packet);
    }

    [Fact]
    public void MapToPacket_InvalidSourceBase_ReturnsNullAndLogsWarning()
    {
        PacketRecord record = CreateRecord("Status");
        record.SourceBase = "BAD!";
        record.RawInfo = "PRIVATE-PAYLOAD-9876";
        record.Status = new StatusDataRecord { Text = "x" };
        RecordingLogger<PacketRecordMapperTests> logger = new();

        AprsPacket? packet = PacketRecordMapper.MapToPacket(record, logger);

        Assert.Null(packet);
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Warning);
        Assert.DoesNotContain(logger.Entries, entry =>
            entry.Message.Contains("PRIVATE-PAYLOAD-9876", StringComparison.Ordinal));
    }

    [Fact]
    public void MapToPacket_PacketTimestamp_ReadsAsUtc()
    {
        PacketRecord record = CreateRecord("Status");
        record.Status = new StatusDataRecord { Text = "x" };

        AprsPacket? packet = PacketRecordMapper.MapToPacket(record);

        StatusPacket status = Assert.IsType<StatusPacket>(packet);
        Assert.Equal(new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero), status.Timestamp);
    }

    [Fact]
    public void MapToPacket_NoPacketTimestamp_TimestampIsNull()
    {
        PacketRecord record = CreateRecord("Status");
        record.PacketTimestamp = null;
        record.Status = new StatusDataRecord { Text = "x" };

        AprsPacket? packet = PacketRecordMapper.MapToPacket(record);

        StatusPacket status = Assert.IsType<StatusPacket>(packet);
        Assert.Null(status.Timestamp);
    }

    [Fact]
    public void ToRecord_CopiesRawFrameAndPath()
    {
        PositionPacket packet = new()
        {
            Source = new Callsign("N0CALL"),
            Destination = new Callsign("APRS"),
            Location = new Coordinate(38.5, -9.0),
            Symbol = new Symbol(SymbolTable.Primary, SymbolCode.HyphenMinus),
            RawFrameHex = "AA0000",
            RawPath = "N0CALL>APRS,WIDE1-1"
        };

        PacketRecord record = PacketRecordMapper.ToRecord(packet, null, DateTimeOffset.UtcNow, isOutbound: false);

        Assert.Equal("AA0000", record.RawFrameHex);
        Assert.Equal("N0CALL>APRS,WIDE1-1", record.RawPath);
    }

    [Fact]
    public void MapToPacket_RestoresRawFrameAndPath()
    {
        PacketRecord record = CreateRecord("Status");
        record.Status = new StatusDataRecord { Text = "x" };
        record.RawFrameHex = "AA0000";
        record.RawPath = "N0CALL>APRS,WIDE1-1";

        AprsPacket? packet = PacketRecordMapper.MapToPacket(record);

        StatusPacket status = Assert.IsType<StatusPacket>(packet);
        Assert.Equal("AA0000", status.RawFrameHex);
        Assert.Equal("N0CALL>APRS,WIDE1-1", status.RawPath);
    }

    [Fact]
    public void MapToPacket_TelemetryRecord_ReturnsTelemetryPacket()
    {
        PacketRecord record = CreateRecord("Telemetry");
        record.Telemetry = new TelemetryDataRecord
        {
            SequenceNumber = 42,
            Analog1 = 1.5,
            Analog2 = 2.5,
            Analog3 = 3.5,
            DigitalValue = 0b101
        };

        AprsPacket? packet = PacketRecordMapper.MapToPacket(record);

        TelemetryPacket telemetry = Assert.IsType<TelemetryPacket>(packet);
        Assert.Equal(42, telemetry.SequenceNumber);
        Assert.Equal([1.5, 2.5, 3.5], telemetry.AnalogValues);
        Assert.Equal((byte?)0b101, telemetry.DigitalValue);
    }

    [Fact]
    public void MapToPacket_TelemetryWithAnInteriorGap_KeepsTheChannelPosition()
    {
        PacketRecord record = CreateRecord("Telemetry");
        record.Telemetry = new TelemetryDataRecord
        {
            SequenceNumber = 3,
            Analog1 = null,
            Analog2 = 20.0,
            Analog3 = null,
            Analog4 = 40.0
        };

        AprsPacket? packet = PacketRecordMapper.MapToPacket(record);

        TelemetryPacket telemetry = Assert.IsType<TelemetryPacket>(packet);
        Assert.Equal(4, telemetry.AnalogValues.Count);
        Assert.Null(telemetry.AnalogValues[0]);
        Assert.Equal(20.0, telemetry.AnalogValues[1]);
        Assert.Null(telemetry.AnalogValues[2]);
        Assert.Equal(40.0, telemetry.AnalogValues[3]);
    }

    [Fact]
    public void ToRecord_ThenMapToPacket_KeepsAnEmptyChannelBetweenValues()
    {
        TelemetryPacket packet = new()
        {
            Source = new Callsign("N0CALL"),
            Destination = new Callsign("APRS"),
            SequenceNumber = 9,
            AnalogValues = [1.0, null, 3.0]
        };

        PacketRecord record = PacketRecordMapper.ToRecord(packet, null, DateTimeOffset.UtcNow, isOutbound: false);
        TelemetryPacket restored = Assert.IsType<TelemetryPacket>(PacketRecordMapper.MapToPacket(record));

        Assert.Equal([1.0, null, 3.0], restored.AnalogValues);
    }

    [Fact]
    public void ToRecord_TelemetryPacket_PersistsAnalogChannels()
    {
        TelemetryPacket packet = new()
        {
            Source = new Callsign("N0CALL"),
            Destination = new Callsign("APRS"),
            SequenceNumber = 7,
            AnalogValues = [10.0, 20.0],
            DigitalValue = 0xF0
        };

        PacketRecord record = PacketRecordMapper.ToRecord(packet, null, DateTimeOffset.UtcNow, isOutbound: false);

        Assert.NotNull(record.Telemetry);
        Assert.Equal(7, record.Telemetry!.SequenceNumber);
        Assert.Equal(10.0, record.Telemetry.Analog1);
        Assert.Equal(20.0, record.Telemetry.Analog2);
        Assert.Null(record.Telemetry.Analog3);
        Assert.Equal((byte?)0xF0, record.Telemetry.DigitalValue);
    }

    [Fact]
    public void MapToPacket_CapabilitiesRecord_ReturnsCapabilitiesPacket()
    {
        PacketRecord record = CreateRecord("Capabilities");
        record.Capabilities = new CapabilitiesDataRecord { Text = "IGATE,MSG_CNT=0" };

        AprsPacket? packet = PacketRecordMapper.MapToPacket(record);

        CapabilitiesPacket capabilities = Assert.IsType<CapabilitiesPacket>(packet);
        Assert.Equal("IGATE,MSG_CNT=0", capabilities.Text);
    }

    [Fact]
    public void ToRecord_CapabilitiesPacket_PersistsText()
    {
        CapabilitiesPacket packet = new()
        {
            Source = new Callsign("N0CALL"),
            Destination = new Callsign("APRS"),
            Text = "IGATE,MSG_CNT=0"
        };

        PacketRecord record = PacketRecordMapper.ToRecord(packet, null, DateTimeOffset.UtcNow, isOutbound: false);

        Assert.NotNull(record.Capabilities);
        Assert.Equal("IGATE,MSG_CNT=0", record.Capabilities!.Text);
    }

    [Fact]
    public void MapToPacket_ObjectRecord_ReturnsObjectPacket()
    {
        PacketRecord record = CreateRecord("Object");
        record.Object = new ObjectDataRecord
        {
            Name = "OBJNAME",
            IsLive = true,
            RawTimestamp = "010203z",
            Location = new Coordinate(38.5, -9.0),
            Altitude = 328.084,
            Course = 90,
            Speed = 10,
            SymbolTable = SymbolTable.Primary,
            SymbolCode = SymbolCode.HyphenMinus,
            Comment = "object comment"
        };

        AprsPacket? packet = PacketRecordMapper.MapToPacket(record);

        ObjectPacket obj = Assert.IsType<ObjectPacket>(packet);
        Assert.Equal("OBJNAME", obj.Name);
        Assert.True(obj.IsLive);
        Assert.Equal("010203z", obj.RawTimestamp);
        Assert.Equal(38.5, obj.Location.Latitude);
        Assert.Equal(-9.0, obj.Location.Longitude);
        Assert.Equal(328.084, obj.Altitude);
        Assert.Equal(90, obj.Course);
        Assert.Equal(10, obj.Speed);
        Assert.Equal(new Symbol(SymbolTable.Primary, SymbolCode.HyphenMinus), obj.Symbol);
        Assert.Equal("object comment", obj.Comment);
        Assert.Equal("raw", obj.Raw);
    }

    [Fact]
    public void ToRecord_ObjectPacket_PersistsFields()
    {
        ObjectPacket packet = new()
        {
            Source = new Callsign("N0CALL"),
            Destination = new Callsign("APRS"),
            Name = "OBJNAME",
            IsLive = false,
            RawTimestamp = "010203z",
            Location = new Coordinate(38.5, -9.0),
            Altitude = 328.084,
            Course = 90,
            Speed = 10,
            Symbol = new Symbol(SymbolTable.Primary, SymbolCode.HyphenMinus),
            Comment = "object comment"
        };

        PacketRecord record = PacketRecordMapper.ToRecord(packet, null, DateTimeOffset.UtcNow, isOutbound: false);

        Assert.Equal("Object", record.PacketType);
        Assert.NotNull(record.Object);
        Assert.Equal("OBJNAME", record.Object!.Name);
        Assert.False(record.Object.IsLive);
        Assert.Equal("010203z", record.Object.RawTimestamp);
        Assert.Equal(38.5, record.Object.Location.Latitude);
        Assert.Equal(-9.0, record.Object.Location.Longitude);
        Assert.Equal(328.084, record.Object.Altitude);
        Assert.Equal(90, record.Object.Course);
        Assert.Equal(10, record.Object.Speed);
        Assert.Equal(SymbolTable.Primary, record.Object.SymbolTable);
        Assert.Equal(SymbolCode.HyphenMinus, record.Object.SymbolCode);
        Assert.Equal("object comment", record.Object.Comment);
    }

    [Fact]
    public void MapToPacket_ItemRecord_ReturnsItemPacket()
    {
        PacketRecord record = CreateRecord("Item");
        record.Item = new ItemDataRecord
        {
            Name = "ITEM",
            IsLive = true,
            Location = new Coordinate(38.5, -9.0),
            Altitude = 328.084,
            Course = 90,
            Speed = 10,
            SymbolTable = SymbolTable.Alternate,
            SymbolCode = SymbolCode.HyphenMinus,
            Comment = "item comment"
        };

        AprsPacket? packet = PacketRecordMapper.MapToPacket(record);

        ItemPacket item = Assert.IsType<ItemPacket>(packet);
        Assert.Equal("ITEM", item.Name);
        Assert.True(item.IsLive);
        Assert.Equal(38.5, item.Location.Latitude);
        Assert.Equal(-9.0, item.Location.Longitude);
        Assert.Equal(328.084, item.Altitude);
        Assert.Equal(90, item.Course);
        Assert.Equal(10, item.Speed);
        Assert.Equal(new Symbol(SymbolTable.Alternate, SymbolCode.HyphenMinus), item.Symbol);
        Assert.Equal("item comment", item.Comment);
    }

    [Fact]
    public void ToRecord_ItemPacket_PersistsFields()
    {
        ItemPacket packet = new()
        {
            Source = new Callsign("N0CALL"),
            Destination = new Callsign("APRS"),
            Name = "ITEM",
            IsLive = false,
            Location = new Coordinate(38.5, -9.0),
            Symbol = new Symbol(SymbolTable.Alternate, SymbolCode.HyphenMinus),
            Comment = "item comment"
        };

        PacketRecord record = PacketRecordMapper.ToRecord(packet, null, DateTimeOffset.UtcNow, isOutbound: false);

        Assert.Equal("Item", record.PacketType);
        Assert.NotNull(record.Item);
        Assert.Equal("ITEM", record.Item!.Name);
        Assert.False(record.Item.IsLive);
        Assert.Equal(38.5, record.Item.Location.Latitude);
        Assert.Equal(-9.0, record.Item.Location.Longitude);
        Assert.Equal(SymbolTable.Alternate, record.Item.SymbolTable);
        Assert.Equal(SymbolCode.HyphenMinus, record.Item.SymbolCode);
        Assert.Equal("item comment", record.Item.Comment);
    }

    [Fact]
    public void MapToPacket_BulletinRecord_ReturnsBulletinPacket()
    {
        PacketRecord record = CreateRecord("Bulletin");
        record.Bulletin = new BulletinDataRecord
        {
            Name = "BLN0SPCL",
            Text = "Special event",
            Number = 5
        };

        AprsPacket? packet = PacketRecordMapper.MapToPacket(record);

        BulletinPacket bulletin = Assert.IsType<BulletinPacket>(packet);
        Assert.Equal("BLN0SPCL", bulletin.Name);
        Assert.Equal("Special event", bulletin.Text);
        Assert.Equal(5, bulletin.MessageNumber);
    }

    [Fact]
    public void ToRecord_BulletinPacket_PersistsFields()
    {
        BulletinPacket packet = new()
        {
            Source = new Callsign("N0CALL"),
            Destination = new Callsign("APRS"),
            Name = "BLN0SPCL",
            Text = "Special event",
            MessageNumber = 5
        };

        PacketRecord record = PacketRecordMapper.ToRecord(packet, null, DateTimeOffset.UtcNow, isOutbound: false);

        Assert.Equal("Bulletin", record.PacketType);
        Assert.NotNull(record.Bulletin);
        Assert.Equal("BLN0SPCL", record.Bulletin!.Name);
        Assert.Equal("Special event", record.Bulletin.Text);
        Assert.Equal(5, record.Bulletin.Number);
    }

    [Fact]
    public void MapToPacket_NullLogger_OnParseFailure_ReturnsNullWithoutThrowing()
    {
        PacketRecord record = CreateRecord("Status");
        record.SourceBase = "BAD!";

        AprsPacket? packet = PacketRecordMapper.MapToPacket(record, NullLogger.Instance);

        Assert.Null(packet);
    }
}
