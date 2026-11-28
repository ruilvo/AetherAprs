// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Models.Aprs;
using AetherAprs.Models.Aprs.Packets;
using AetherAprs.Modems.Aprs;
using Geo;
using Xunit;

namespace AetherAprs.Tests.Modems.Aprs;

public class MicESerializerTests
{
    private static readonly Callsign Source = new("N0CALL");

    // ---------------------------------------------------------------
    // Destination (latitude) encoding
    // ---------------------------------------------------------------

    [Fact]
    public void EncodeDestination_NorthEast_EncodesPlainDigits()
    {
        // 42°30.00'N, 8°30.00'E: no offset, no W/E flag, no S flag.
        PositionPacket packet = new()
        {
            Source = Source,
            Destination = new Callsign("APRS"),
            Location = new Coordinate(42.5, 8.5),
            Symbol = new Symbol(SymbolTable.Primary, SymbolCode.GreaterThanSign),
            Format = PositionFormat.MicE
        };

        Callsign destination = MicESerializer.EncodeDestination(packet);

        // North is the flagged state in the fourth destination character.
        Assert.Equal("423P00", destination.Base);
    }

    [Fact]
    public void EncodeDestination_SouthWestWithOffset_EncodesFlagCharacters()
    {
        // 42°30.00'S, 108°30.00'W: south flag, longitude offset (>=100), west flag.
        PositionPacket packet = new()
        {
            Source = Source,
            Destination = new Callsign("APRS"),
            Location = new Coordinate(-42.5, -108.5),
            Symbol = new Symbol(SymbolTable.Primary, SymbolCode.GreaterThanSign),
            Format = PositionFormat.MicE
        };

        Callsign destination = MicESerializer.EncodeDestination(packet);

        // South is the unflagged state in the fourth character; the fifth and
        // sixth carry the longitude offset and west flags.
        Assert.Equal("4230PP", destination.Base);
    }

    // ---------------------------------------------------------------
    // Info field round-trip via the parser
    // ---------------------------------------------------------------

    [Fact]
    public void FormatInfoField_RoundTripsThroughParser()
    {
        PositionPacket packet = new()
        {
            Source = Source,
            Destination = new Callsign("APRS"),
            Location = new Coordinate(42.5085, -8.5085),
            Symbol = new Symbol(SymbolTable.Primary, SymbolCode.GreaterThanSign),
            Course = 180,
            Speed = 12,
            Format = PositionFormat.MicE
        };

        string infoField = MicESerializer.FormatInfoField(packet);
        Callsign destination = MicESerializer.EncodeDestination(packet);

        AprsPacket parsed = AprsInfoFieldParser.ParseInfoField(infoField, Source, destination);

        PositionPacket pos = Assert.IsType<PositionPacket>(parsed);
        Assert.Equal(42.5085, pos.Location.Latitude, 3);
        Assert.Equal(-8.5085, pos.Location.Longitude, 3);
        Assert.Equal(SymbolTable.Primary, pos.Symbol.Table);
        Assert.Equal(SymbolCode.GreaterThanSign, pos.Symbol.Code);
        Assert.Equal(180, pos.Course);
        Assert.NotNull(pos.Speed);
        Assert.Equal(12, pos.Speed.Value, 1);
    }

    [Fact]
    public void FormatInfoField_WithAltitude_RoundTripsAltitude()
    {
        PositionPacket packet = new()
        {
            Source = Source,
            Destination = new Callsign("APRS"),
            Location = new Coordinate(42.5085, -8.5085),
            Symbol = new Symbol(SymbolTable.Primary, SymbolCode.GreaterThanSign),
            Altitude = 500,
            Format = PositionFormat.MicE
        };

        string infoField = MicESerializer.FormatInfoField(packet);
        Callsign destination = MicESerializer.EncodeDestination(packet);

        AprsPacket parsed = AprsInfoFieldParser.ParseInfoField(infoField, Source, destination);

        PositionPacket pos = Assert.IsType<PositionPacket>(parsed);
        Assert.NotNull(pos.Altitude);
        // The wire carries whole meters, so a feet value round-trips within
        // the rounding of one metre.
        Assert.InRange(pos.Altitude.Value, 497, 500);
    }

    // ---------------------------------------------------------------
    // Full-frame round-trip via AX.25
    // ---------------------------------------------------------------

    [Fact]
    public void Ax25Serialize_MicE_EncodesLatitudeIntoDestination()
    {
        PositionPacket packet = new()
        {
            Source = Source,
            Destination = new Callsign("APRS"),
            Location = new Coordinate(42.5, 8.5),
            Symbol = new Symbol(SymbolTable.Primary, SymbolCode.GreaterThanSign),
            Format = PositionFormat.MicE
        };

        byte[] frame = Ax25Serializer.Serialize(packet);
        AprsPacket parsed = Ax25Parser.ParseFrame(frame);

        PositionPacket pos = Assert.IsType<PositionPacket>(parsed);
        Assert.Equal(42.5, pos.Location.Latitude, 2);
        Assert.Equal(8.5, pos.Location.Longitude, 2);
    }
}
