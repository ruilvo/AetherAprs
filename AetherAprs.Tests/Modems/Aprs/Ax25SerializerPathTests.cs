// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Models.Aprs;
using AetherAprs.Models.Aprs.Packets;
using AetherAprs.Modems.Aprs;
using System;
using Geo;
using Xunit;

namespace AetherAprs.Tests.Modems.Aprs;

public class Ax25SerializerPathTests
{
    [Fact]
    public void Serialize_PacketWithEmptyPath_EncodesCorrectly()
    {
        var packet = new PositionPacket
        {
            Source = new Callsign("N0CALL", 5),
            Destination = new Callsign("APRS"),
            Path = Array.Empty<Callsign>(),
            Location = new Coordinate(37.7749, -122.4194),
            Symbol = new Symbol(SymbolTable.Primary, SymbolCode.LeftSquareBracket),
            Precision = 2
        };

        var result = Ax25Serializer.Serialize(packet);

        // Should have: destination (7) + source (7) + control (1) + PID (1) + info
        Assert.True(result.Length >= 16);
        
        // Verify source is marked as last address (bit 0 = 1)
        Assert.Equal(1, result[13] & 0x01);
    }

    [Fact]
    public void Serialize_PacketWithSingleDigipeater_EncodesCorrectly()
    {
        var packet = new PositionPacket
        {
            Source = new Callsign("N0CALL", 5),
            Destination = new Callsign("APRS"),
            Path = new[] { new Callsign("WIDE1", 1) },
            Location = new Coordinate(37.7749, -122.4194),
            Symbol = new Symbol(SymbolTable.Primary, SymbolCode.LeftSquareBracket),
            Precision = 2
        };

        var result = Ax25Serializer.Serialize(packet);

        // Should have: destination (7) + source (7) + path (7) + control (1) + PID (1) + info
        Assert.True(result.Length >= 23);
        
        // Verify source is NOT marked as last address (bit 0 = 0)
        Assert.Equal(0, result[13] & 0x01);
        
        // Verify path entry is marked as last address (bit 0 = 1)
        Assert.Equal(1, result[20] & 0x01);
    }

    [Fact]
    public void Serialize_PacketWithMultipleDigipeaters_EncodesCorrectly()
    {
        var packet = new PositionPacket
        {
            Source = new Callsign("N0CALL", 5),
            Destination = new Callsign("APRS"),
            Path = new[] { new Callsign("WIDE1", 1), new Callsign("WIDE2", 1) },
            Location = new Coordinate(37.7749, -122.4194),
            Symbol = new Symbol(SymbolTable.Primary, SymbolCode.LeftSquareBracket),
            Precision = 2
        };

        var result = Ax25Serializer.Serialize(packet);

        // Should have: destination (7) + source (7) + path1 (7) + path2 (7) + control (1) + PID (1) + info
        Assert.True(result.Length >= 30);
        
        // Verify source is NOT marked as last address
        Assert.Equal(0, result[13] & 0x01);
        
        // Verify first path entry is NOT marked as last address
        Assert.Equal(0, result[20] & 0x01);
        
        // Verify second path entry IS marked as last address
        Assert.Equal(1, result[27] & 0x01);
    }

    [Fact]
    public void Serialize_WithExplicitPath_UsesProvidedPath()
    {
        var packet = new PositionPacket
        {
            Source = new Callsign("N0CALL", 5),
            Destination = new Callsign("APRS"),
            Path = Array.Empty<Callsign>(), // Empty in packet
            Location = new Coordinate(37.7749, -122.4194),
            Symbol = new Symbol(SymbolTable.Primary, SymbolCode.LeftSquareBracket),
            Precision = 2
        };

        var explicitPath = new[] { new Callsign("WIDE1", 1) };
        var result = Ax25Serializer.Serialize(packet, packet.Source, packet.Destination, explicitPath);

        // Should include the explicit path, not the packet's empty path
        Assert.True(result.Length >= 23);
        
        // Verify path entry is marked as last address
        Assert.Equal(1, result[20] & 0x01);
    }

    [Fact]
    public void ParseAndSerialize_RoundTrip_PreservesPath()
    {
        var original = new PositionPacket
        {
            Source = new Callsign("N0CALL", 5),
            Destination = new Callsign("APRS"),
            Path = new[] { new Callsign("WIDE1", 1), new Callsign("WIDE2", 1) },
            Location = new Coordinate(37.7749, -122.4194),
            Symbol = new Symbol(SymbolTable.Primary, SymbolCode.LeftSquareBracket),
            Precision = 2
        };

        var serialized = Ax25Serializer.Serialize(original);
        var parsed = Ax25Parser.ParseFrame(serialized);

        Assert.Equal(original.Source.Base, parsed.Source.Base);
        Assert.Equal(original.Source.Ssid, parsed.Source.Ssid);
        Assert.Equal(original.Destination.Base, parsed.Destination.Base);
        Assert.Equal(2, parsed.Path.Count);
        Assert.Equal("WIDE1", parsed.Path[0].Base);
        Assert.Equal(1, parsed.Path[0].Ssid);
        Assert.Equal("WIDE2", parsed.Path[1].Base);
        Assert.Equal(1, parsed.Path[1].Ssid);
    }

    [Fact]
    public void Serialize_DefaultCallsign_ThrowsArgumentException()
    {
        var packet = new PositionPacket
        {
            Source = new Callsign("N0CALL", 5),
            Destination = new Callsign("APRS"),
            Path = [default(Callsign)],
            Location = new Coordinate(37.7749, -122.4194),
            Symbol = new Symbol(SymbolTable.Primary, SymbolCode.LeftSquareBracket),
            Precision = 2
        };

        Assert.Throws<ArgumentException>(() => Ax25Serializer.Serialize(packet));
    }

    [Fact]
    public void ParseFrame_DigipeaterWithHBitSet_PreservesHasBeenRepeated()
    {
        // Serialize a normal frame, then set the H bit (bit 7) on the first
        // digipeater's SSID byte to simulate a hop that has already been repeated.
        var packet = new PositionPacket
        {
            Source = new Callsign("N0CALL"),
            Destination = new Callsign("APRS"),
            Path = [new Callsign("WIDE1", 1), new Callsign("WIDE2", 1)],
            Location = new Coordinate(37.7749, -122.4194),
            Symbol = new Symbol(SymbolTable.Primary, SymbolCode.LeftSquareBracket),
            Precision = 2
        };

        var frame = Ax25Serializer.Serialize(packet);

        // Destination occupies bytes 0..6, source 7..13, first digipeater 14..20.
        // The SSID byte of the first digipeater is at offset 20.
        const int firstDigiSsidByte = 20;
        frame[firstDigiSsidByte] |= 0x80;

        var parsed = Ax25Parser.ParseFrame(frame);

        Assert.Equal(2, parsed.Path.Count);
        Assert.True(parsed.Path[0].HasBeenRepeated);
        Assert.False(parsed.Path[1].HasBeenRepeated);
    }

    [Fact]
    public void Serialize_DigipeaterWithHasBeenRepeated_SetsHBit()
    {
        var packet = new PositionPacket
        {
            Source = new Callsign("N0CALL"),
            Destination = new Callsign("APRS"),
            Path = [new Callsign("WIDE1", 1, hasBeenRepeated: true)],
            Location = new Coordinate(37.7749, -122.4194),
            Symbol = new Symbol(SymbolTable.Primary, SymbolCode.LeftSquareBracket),
            Precision = 2
        };

        var frame = Ax25Serializer.Serialize(packet);

        // Destination 0..6, source 7..13, first digipeater 14..20; its SSID byte is
        // at offset 20 and its H (has-been-repeated) flag is bit 7.
        const int firstDigiSsidByte = 20;
        Assert.NotEqual(0, frame[firstDigiSsidByte] & 0x80);

        // Round-trip preserves the flag.
        var parsed = Ax25Parser.ParseFrame(frame);
        Assert.True(parsed.Path[0].HasBeenRepeated);
    }

    [Fact]
    public void Serialize_DigipeaterWithoutHasBeenRepeated_LeavesHBitClear()
    {
        var packet = new PositionPacket
        {
            Source = new Callsign("N0CALL"),
            Destination = new Callsign("APRS"),
            Path = [new Callsign("WIDE1", 1)],
            Location = new Coordinate(37.7749, -122.4194),
            Symbol = new Symbol(SymbolTable.Primary, SymbolCode.LeftSquareBracket),
            Precision = 2
        };

        var frame = Ax25Serializer.Serialize(packet);

        const int firstDigiSsidByte = 20;
        Assert.Equal(0, frame[firstDigiSsidByte] & 0x80);
    }

    [Fact]
    public void ParseFrame_DigipeaterWithoutHBit_HasBeenRepeatedIsFalse()
    {
        var packet = new PositionPacket
        {
            Source = new Callsign("N0CALL"),
            Destination = new Callsign("APRS"),
            Path = [new Callsign("WIDE1", 1)],
            Location = new Coordinate(37.7749, -122.4194),
            Symbol = new Symbol(SymbolTable.Primary, SymbolCode.LeftSquareBracket),
            Precision = 2
        };

        var frame = Ax25Serializer.Serialize(packet);

        var parsed = Ax25Parser.ParseFrame(frame);

        var pos = Assert.IsType<PositionPacket>(parsed);
        Assert.False(pos.Path[0].HasBeenRepeated);
        Assert.False(pos.Source.HasBeenRepeated);
    }
}
