// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

namespace AetherAprs.Tests;

using AetherAprs.Models;
using AetherAprs.Protocols.Aprs;
using AetherAprs.Protocols.AprsIs;
using AetherAprs.Protocols.Ax25;
using AetherAprs.Protocols.Kiss;
using NetTopologySuite.Geometries;
using System.Text;
using Xunit;

public sealed class AprsCodecTests
{
    [Theory]
    [InlineData(AprsStationDataType.PositionWithoutMessaging, '!')]
    [InlineData(AprsStationDataType.PositionWithMessaging, '=')]
    [InlineData(AprsStationDataType.TimestampedPositionWithoutMessaging, '/')]
    [InlineData(AprsStationDataType.TimestampedPositionWithMessaging, '@')]
    public void Station_payload_round_trip_preserves_station_data_type(AprsStationDataType stationDataType, char expectedPrefix)
    {
        var entry = CreateStationPayload(stationDataType);

        var payload = AprsPayloadCodec.Encode(entry);
        var decoded = AprsPayloadCodec.Decode(payload);

        Assert.Equal(expectedPrefix, payload[0]);
        Assert.Equal(stationDataType, decoded.StationDataType);
        AssertPayloadStation(entry, decoded);
    }

    [Fact]
    public void AprsIs_round_trip_preserves_packet_path_and_route_separately()
    {
        var packet = "N0CALL-1>APRS,WIDE1-1,qAR,TESTIGATE:/231234z3843.34N/00908.36W>station";

        var decoded = AprsIsCodec.Decode(packet);
        var encoded = AprsIsCodec.Encode(decoded);

        Assert.Equal("N0CALL-1", decoded.Packet.Source);
        Assert.Equal("APRS", decoded.Packet.Destination);
        Assert.Equal(["WIDE1-1"], decoded.Packet.Path);
        Assert.Equal(["qAR", "TESTIGATE"], decoded.Route);
        Assert.Equal(packet, encoded);
    }

    [Fact]
    public void Kiss_round_trip_preserves_packet_addressing_and_item_payload()
    {
        var packet = new AprsPacket
        {
            Source = "N0CALL-1",
            Destination = "APRS",
            Path = ["WIDE1-1"],
            Payload = new AprsEntry
            {
                Kind = AprsEntryKind.Item,
                ItemName = "TESTITEM",
                IsAlive = false,
                Location = new Point(-9.1393, 38.7223),
                Symbol = new AprsSymbol { Table = AprsSymbolTable.Secondary, Code = 'k', Overlay = 'A' },
                Comment = "item",
            },
        };

        var frame = Ax25AprsKissCodec.Encode(packet);
        var decoded = Ax25AprsKissCodec.Decode(frame);

        Assert.Equal(packet.Source, decoded.Source);
        Assert.Equal(packet.Destination, decoded.Destination);
        Assert.Equal(packet.Path, decoded.Path);
        Assert.Equal(packet.Payload.Kind, decoded.Payload.Kind);
        Assert.Equal(packet.Payload.ItemName, decoded.Payload.ItemName);
        Assert.Equal(packet.Payload.IsAlive, decoded.Payload.IsAlive);
        Assert.Equal(packet.Payload.Comment, decoded.Payload.Comment);
        Assert.Equal(packet.Payload.Symbol!.Code, decoded.Payload.Symbol!.Code);
        Assert.Equal(packet.Payload.Symbol.Overlay, decoded.Payload.Symbol!.Overlay);
        AssertLocation(packet.Payload.Location!, decoded.Payload.Location!);
    }

    [Fact]
    public void AprsIs_round_trip_preserves_object_timestamp_without_guessing_month_or_year()
    {
        var packet = new AprsIsPacket
        {
            Packet = new AprsPacket
            {
                Source = "N0CALL-1",
                Destination = "APRS",
                Path = ["WIDE1-1", "WIDE2-1"],
                Payload = new AprsEntry
                {
                    Kind = AprsEntryKind.Object,
                    ObjectName = "TESTOBJ",
                    IsAlive = true,
                    AprsTimestamp = new AprsPartialTimestamp { Day = 23, Hour = 12, Minute = 34, Suffix = 'z' },
                    Location = new Point(-9.1393, 38.7223),
                    Symbol = new AprsSymbol { Table = AprsSymbolTable.Primary, Code = '>' },
                    Comment = "object",
                },
            },
        };

        var encoded = AprsIsCodec.Encode(packet);
        var decoded = AprsIsCodec.Decode(encoded);

        Assert.Equal(packet.Packet.Payload.AprsTimestamp!.Day, decoded.Packet.Payload.AprsTimestamp!.Day);
        Assert.Equal(packet.Packet.Payload.AprsTimestamp.Hour, decoded.Packet.Payload.AprsTimestamp.Hour);
        Assert.Equal(packet.Packet.Payload.AprsTimestamp.Minute, decoded.Packet.Payload.AprsTimestamp.Minute);
        Assert.Equal(packet.Packet.Payload.AprsTimestamp.Suffix, decoded.Packet.Payload.AprsTimestamp.Suffix);
    }

    [Fact]
    public void Message_recipients_longer_than_nine_characters_are_rejected()
    {
        var entry = new AprsEntry
        {
            Kind = AprsEntryKind.Message,
            MessageRecipient = "ABCDEFGHIJ",
            MessageText = "hello world",
        };

        Assert.Throws<InvalidOperationException>(() => AprsPayloadCodec.Encode(entry));
    }

    [Fact]
    public void Object_names_longer_than_nine_characters_are_rejected()
    {
        var entry = new AprsEntry
        {
            Kind = AprsEntryKind.Object,
            ObjectName = "ABCDEFGHIJ",
            AprsTimestamp = new AprsPartialTimestamp { Day = 23, Hour = 12, Minute = 34, Suffix = 'z' },
            Location = new Point(-9.1393, 38.7223),
            Symbol = new AprsSymbol { Table = AprsSymbolTable.Primary, Code = '>' },
        };

        Assert.Throws<InvalidOperationException>(() => AprsPayloadCodec.Encode(entry));
    }

    [Fact]
    public void Invalid_aprs_timestamp_is_rejected()
    {
        Assert.Throws<InvalidOperationException>(() => AprsPayloadCodec.Decode("/321260z3843.34N/00908.36W>station"));
    }

    [Fact]
    public void Invalid_item_name_separator_is_rejected()
    {
        Assert.Throws<InvalidOperationException>(() => AprsPayloadCodec.Decode(")ABCDEFGHIJK!3843.34N/00908.36W>item"));
    }

    [Fact]
    public void Invalid_ax25_callsign_is_rejected()
    {
        Assert.Throws<InvalidOperationException>(() => Ax25Codec.ParseAddress("TOOLONG1-1"));
    }

    [Fact]
    public void Invalid_ax25_ssid_is_rejected()
    {
        Assert.Throws<InvalidOperationException>(() => Ax25Codec.ParseAddress("N0CALL-16"));
    }

    [Fact]
    public void Non_ui_ax25_frame_is_rejected_for_aprs_decode()
    {
        var payload = AprsPayloadCodec.Encode(CreateStationPayload(AprsStationDataType.TimestampedPositionWithoutMessaging));
        var ax25Frame = new Ax25Frame(
            Ax25Codec.ParseAddress("APRS"),
            Ax25Codec.ParseAddress("N0CALL-1"),
            [],
            0x13,
            Ax25Codec.NoLayer3ProtocolId,
            Encoding.ASCII.GetBytes(payload));
        var kissFrame = KissCodec.Encode(new KissFrame(0, 0, Ax25Codec.Encode(ax25Frame)));

        Assert.Throws<InvalidOperationException>(() => Ax25AprsKissCodec.Decode(kissFrame));
    }

    [Fact]
    public void Non_f0_pid_ax25_frame_is_rejected_for_aprs_decode()
    {
        var payload = AprsPayloadCodec.Encode(CreateStationPayload(AprsStationDataType.TimestampedPositionWithoutMessaging));
        var ax25Frame = new Ax25Frame(
            Ax25Codec.ParseAddress("APRS"),
            Ax25Codec.ParseAddress("N0CALL-1"),
            [],
            Ax25Codec.UiFrameControl,
            0xCF,
            Encoding.ASCII.GetBytes(payload));
        var kissFrame = KissCodec.Encode(new KissFrame(0, 0, Ax25Codec.Encode(ax25Frame)));

        Assert.Throws<InvalidOperationException>(() => Ax25AprsKissCodec.Decode(kissFrame));
    }

    private static AprsEntry CreateStationPayload(AprsStationDataType stationDataType) => new()
    {
        Kind = AprsEntryKind.Station,
        Location = new Point(-9.1393, 38.7223),
        Symbol = new AprsSymbol { Table = AprsSymbolTable.Primary, Code = '>' },
        StationDataType = stationDataType,
        AprsTimestamp = stationDataType is AprsStationDataType.TimestampedPositionWithoutMessaging or AprsStationDataType.TimestampedPositionWithMessaging
            ? new AprsPartialTimestamp { Day = 23, Hour = 12, Minute = 34, Suffix = 'z' }
            : null,
        Comment = "station",
    };

    private static void AssertPayloadStation(AprsEntry expected, AprsEntry actual)
    {
        Assert.Equal(expected.Kind, actual.Kind);
        Assert.Equal(expected.Comment, actual.Comment);
        Assert.Equal(expected.Symbol!.Code, actual.Symbol!.Code);
        Assert.Equal(expected.Symbol.Table, actual.Symbol.Table);
        Assert.Equal(expected.StationDataType, actual.StationDataType);
        AssertLocation(expected.Location!, actual.Location!);

        if (expected.AprsTimestamp is null)
        {
            Assert.Null(actual.AprsTimestamp);
        }
        else
        {
            Assert.Equal(expected.AprsTimestamp.Day, actual.AprsTimestamp!.Day);
            Assert.Equal(expected.AprsTimestamp.Hour, actual.AprsTimestamp.Hour);
            Assert.Equal(expected.AprsTimestamp.Minute, actual.AprsTimestamp.Minute);
            Assert.Equal(expected.AprsTimestamp.Suffix, actual.AprsTimestamp.Suffix);
        }
    }

    private static void AssertLocation(Point expected, Point actual)
    {
        Assert.Equal(expected.X, actual.X, 4);
        Assert.Equal(expected.Y, actual.Y, 4);
    }
}
