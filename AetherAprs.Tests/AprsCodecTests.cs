// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

namespace AetherAprs.Tests;

using AetherAprs.Models;
using AetherAprs.Protocols.AprsIs;
using AetherAprs.Protocols.Ax25;
using NetTopologySuite.Geometries;
using Xunit;

public sealed class AprsCodecTests
{
    [Fact]
    public void AprsIs_round_trip_preserves_station_entry()
    {
        var entry = CreateStationEntry();

        var packet = AprsIsCodec.Encode(entry);
        var decoded = AprsIsCodec.Decode(packet);

        AssertStation(entry, decoded, assertTimestamp: true);
    }

    [Fact]
    public void Kiss_round_trip_preserves_station_entry()
    {
        var entry = CreateStationEntry();

        var frame = Ax25AprsKissCodec.Encode(entry);
        var decoded = Ax25AprsKissCodec.Decode(frame);

        AssertStation(entry, decoded, assertTimestamp: true);
    }

    [Fact]
    public void AprsIs_round_trip_preserves_object_entry()
    {
        var entry = new AprsEntry
        {
            Kind = AprsEntryKind.Object,
            Source = "N0CALL-1",
            Destination = "APRS",
            Path = ["WIDE1-1", "WIDE2-1"],
            ObjectName = "TESTOBJ",
            IsAlive = true,
            Timestamp = UtcTimestamp(),
            Location = new Point(-9.1393, 38.7223),
            Symbol = new AprsSymbol { Table = AprsSymbolTable.Primary, Code = '>' },
            Comment = "object",
        };

        var packet = AprsIsCodec.Encode(entry);
        var decoded = AprsIsCodec.Decode(packet);

        Assert.Equal(entry.Kind, decoded.Kind);
        Assert.Equal(entry.Source, decoded.Source);
        Assert.Equal(entry.Destination, decoded.Destination);
        Assert.Equal(entry.Path, decoded.Path);
        Assert.Equal(entry.ObjectName, decoded.ObjectName);
        Assert.Equal(entry.IsAlive, decoded.IsAlive);
        Assert.Equal(entry.Comment, decoded.Comment);
        Assert.Equal(entry.Symbol!.Code, decoded.Symbol!.Code);
        Assert.Equal(entry.Symbol.Table, decoded.Symbol.Table);
        Assert.Equal(entry.Timestamp!.Value.Day, decoded.Timestamp!.Value.Day);
        Assert.Equal(entry.Timestamp!.Value.Hour, decoded.Timestamp!.Value.Hour);
        Assert.Equal(entry.Timestamp!.Value.Minute, decoded.Timestamp!.Value.Minute);
        AssertLocation(entry.Location!, decoded.Location!);
    }

    [Fact]
    public void Kiss_round_trip_preserves_item_entry()
    {
        var entry = new AprsEntry
        {
            Kind = AprsEntryKind.Item,
            Source = "N0CALL-1",
            Destination = "APRS",
            Path = ["WIDE1-1"],
            ItemName = "TESTITEM",
            IsAlive = false,
            Location = new Point(-9.1393, 38.7223),
            Symbol = new AprsSymbol { Table = AprsSymbolTable.Secondary, Code = 'k', Overlay = 'A' },
            Comment = "item",
        };

        var frame = Ax25AprsKissCodec.Encode(entry);
        var decoded = Ax25AprsKissCodec.Decode(frame);

        Assert.Equal(entry.Kind, decoded.Kind);
        Assert.Equal(entry.Source, decoded.Source);
        Assert.Equal(entry.Destination, decoded.Destination);
        Assert.Equal(entry.Path, decoded.Path);
        Assert.Equal(entry.ItemName, decoded.ItemName);
        Assert.Equal(entry.IsAlive, decoded.IsAlive);
        Assert.Equal(entry.Comment, decoded.Comment);
        Assert.Equal(entry.Symbol!.Code, decoded.Symbol!.Code);
        Assert.Equal(entry.Symbol.Overlay, decoded.Symbol!.Overlay);
        AssertLocation(entry.Location!, decoded.Location!);
    }

    [Fact]
    public void Both_backends_round_trip_message_entry()
    {
        var entry = new AprsEntry
        {
            Kind = AprsEntryKind.Message,
            Source = "N0CALL-1",
            Destination = "APRS",
            Path = ["TCPIP*"],
            MessageRecipient = "CQ",
            MessageText = "hello world",
            MessageId = "42",
        };

        var aprsIsPacket = AprsIsCodec.Encode(entry);
        var aprsIsDecoded = AprsIsCodec.Decode(aprsIsPacket);
        var kissFrame = Ax25AprsKissCodec.Encode(entry);
        var kissDecoded = Ax25AprsKissCodec.Decode(kissFrame);

        AssertMessage(entry, aprsIsDecoded);
        AssertMessage(entry, kissDecoded);
    }

    [Fact]
    public void Station_requires_symbol()
    {
        var entry = new AprsEntry
        {
            Kind = AprsEntryKind.Station,
            Source = "N0CALL-1",
            Destination = "APRS",
            Location = new Point(-9.1393, 38.7223),
        };

        Assert.Throws<InvalidOperationException>(entry.Validate);
    }

    private static AprsEntry CreateStationEntry() => new()
    {
        Kind = AprsEntryKind.Station,
        Source = "N0CALL-1",
        Destination = "APRS",
        Path = ["WIDE1-1", "WIDE2-1"],
        Location = new Point(-9.1393, 38.7223),
        Symbol = new AprsSymbol { Table = AprsSymbolTable.Primary, Code = '>' },
        Timestamp = UtcTimestamp(),
        Comment = "station",
    };

    private static DateTimeOffset UtcTimestamp()
    {
        var now = DateTimeOffset.UtcNow;
        return new DateTimeOffset(now.Year, now.Month, Math.Min(now.Day, 28), 12, 34, 0, TimeSpan.Zero);
    }

    private static void AssertStation(AprsEntry expected, AprsEntry actual, bool assertTimestamp)
    {
        Assert.Equal(expected.Kind, actual.Kind);
        Assert.Equal(expected.Source, actual.Source);
        Assert.Equal(expected.Destination, actual.Destination);
        Assert.Equal(expected.Path, actual.Path);
        Assert.Equal(expected.Comment, actual.Comment);
        Assert.Equal(expected.Symbol!.Code, actual.Symbol!.Code);
        Assert.Equal(expected.Symbol.Table, actual.Symbol.Table);
        AssertLocation(expected.Location!, actual.Location!);

        if (assertTimestamp)
        {
            Assert.Equal(expected.Timestamp!.Value.Day, actual.Timestamp!.Value.Day);
            Assert.Equal(expected.Timestamp!.Value.Hour, actual.Timestamp!.Value.Hour);
            Assert.Equal(expected.Timestamp!.Value.Minute, actual.Timestamp!.Value.Minute);
        }
    }

    private static void AssertMessage(AprsEntry expected, AprsEntry actual)
    {
        Assert.Equal(expected.Kind, actual.Kind);
        Assert.Equal(expected.Source, actual.Source);
        Assert.Equal(expected.Destination, actual.Destination);
        Assert.Equal(expected.Path, actual.Path);
        Assert.Equal(expected.MessageRecipient, actual.MessageRecipient);
        Assert.Equal(expected.MessageText, actual.MessageText);
        Assert.Equal(expected.MessageId, actual.MessageId);
    }

    private static void AssertLocation(Point expected, Point actual)
    {
        Assert.Equal(expected.X, actual.X, 4);
        Assert.Equal(expected.Y, actual.Y, 4);
    }
}
