// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Linq;
using AetherAprs.Data.Entities;
using AetherAprs.Localization;
using AetherAprs.Models.Aprs;
using AetherAprs.ViewModels.Components;
using Geo;
using Xunit;

namespace AetherAprs.Tests.ViewModels.Components;

public sealed class PacketDetailItemViewModelTests : TestFixtureBase
{
    private static PacketRecord CreatePacket() => new()
    {
        Id = 1,
        SourceBase = "N0CALL",
        SourceSsid = AprsSsid.AdditionalStation1,
        PacketType = "Position",
        Timestamp = new DateTime(2026, 11, 27, 12, 30, 0, DateTimeKind.Utc),
        RawPath = "N0CALL-1>APRS,WIDE1-1,WIDE2-1",
        RawInfo = "!3853.00N/00908.00W-test"
    };

    private static string ValueOf(PacketDetailItemViewModel packet, string labelKey) =>
        packet.DecodedFields.Concat(packet.Provenance)
            .Single(field => field.Label == Strings.Get(labelKey))
            .Value;

    [Fact]
    public void ReceiptAndProtocolTimes_AreReportedSeparately()
    {
        PacketRecord record = CreatePacket();
        // The station's own timestamp is three minutes older than the moment the
        // frame reached this station; the page must never merge the two.
        record.PacketTimestamp = record.Timestamp.AddMinutes(-3);

        PacketDetailItemViewModel packet = new(record, portName: null);

        Assert.True(packet.HasProtocolTime);
        Assert.NotEqual(packet.ReceivedAt, packet.ProtocolTime);
        Assert.Equal(record.Timestamp, packet.ReceivedAt.UtcDateTime);
        Assert.Equal(record.PacketTimestamp!.Value, packet.ProtocolTime!.Value.UtcDateTime);
        Assert.Equal(packet.ReceivedAtText, ValueOf(packet, "PacketDetailReceivedAt"));
        Assert.Equal(packet.ProtocolTimeText, ValueOf(packet, "PacketDetailProtocolTime"));
    }

    [Fact]
    public void PacketWithoutProtocolTimestamp_OmitsThatProvenanceRow()
    {
        PacketDetailItemViewModel packet = new(CreatePacket(), portName: null);

        Assert.False(packet.HasProtocolTime);
        Assert.Empty(packet.ProtocolTimeText);
        Assert.DoesNotContain(
            packet.Provenance,
            field => field.Label == Strings.Get("PacketDetailProtocolTime"));
    }

    [Fact]
    public void Provenance_NamesThePortAndKeepsThePathVerbatim()
    {
        PacketDetailItemViewModel packet = new(CreatePacket(), portName: "APRS-IS");

        Assert.Equal("APRS-IS", ValueOf(packet, "Port"));
        Assert.Equal("N0CALL-1>APRS,WIDE1-1,WIDE2-1", ValueOf(packet, "PacketDetailPath"));
        Assert.Equal(Strings.Get("PacketDetailReceived"), ValueOf(packet, "PacketDetailDirection"));
    }

    [Fact]
    public void Provenance_UnknownPortStillRendersALabel()
    {
        PacketDetailItemViewModel packet = new(CreatePacket(), portName: null);

        Assert.Equal(Strings.Get("PacketDetailPortUnknown"), ValueOf(packet, "Port"));
    }

    [Fact]
    public void OutboundPacket_IsMarkedSent()
    {
        PacketRecord record = CreatePacket();
        record.IsOutbound = true;

        PacketDetailItemViewModel packet = new(record, portName: null);

        Assert.Equal(Strings.Get("PacketDetailSent"), ValueOf(packet, "PacketDetailDirection"));
    }

    [Fact]
    public void Weather_IsDecodedIntoEveryPresentMeasurement()
    {
        PacketRecord record = CreatePacket();
        record.PacketType = "Weather";
        record.Weather = new WeatherDataRecord
        {
            Temperature = 72.5,
            WindSpeed = 12.0,
            WindDirection = 270,
            Humidity = 55,
            Pressure = 1013.2,
            RainLastHour = 0.5,
            RainLast24Hours = 1.25
        };

        PacketDetailItemViewModel packet = new(record, portName: null);

        Assert.True(packet.HasDecodedFields);
        Assert.Equal(
            Strings.Format("PacketDetailTemperatureFormat", 72.5),
            ValueOf(packet, "Temperature"));
        Assert.Equal(
            Strings.Format("PacketDetailWindDirectionFormat", 270),
            ValueOf(packet, "WindDirection"));
        Assert.Equal(
            Strings.Format("PacketDetailInchesFormat", 1.25),
            ValueOf(packet, "RainLast24Hours"));
    }

    [Fact]
    public void Telemetry_ExplainsTheSequenceAndChannels()
    {
        PacketRecord record = CreatePacket();
        record.PacketType = "Telemetry";
        record.Telemetry = new TelemetryDataRecord
        {
            SequenceNumber = 42,
            Analog1 = 1.5,
            DigitalValue = 5
        };

        PacketDetailItemViewModel packet = new(record, portName: null);

        Assert.Equal("42", ValueOf(packet, "TelemetrySequence"));
        Assert.Equal(
            Strings.Format("PacketDetailScalarFormat", 1.5),
            ValueOf(packet, "TelemetryAnalog1"));
        Assert.Equal("5", ValueOf(packet, "TelemetryDigital"));
        // Channels that were not transmitted stay absent instead of rendering zero.
        Assert.DoesNotContain(
            packet.DecodedFields,
            field => field.Label == Strings.Get("TelemetryAnalog2"));
    }

    [Fact]
    public void Object_ReportsNameLivenessAndPosition()
    {
        PacketRecord record = CreatePacket();
        record.PacketType = "Object";
        record.Object = new ObjectDataRecord
        {
            Name = "SHELTER1",
            IsLive = false,
            Location = new Coordinate(38.5, -9.0),
            Altitude = 120,
            SymbolTable = SymbolTable.Primary,
            SymbolCode = SymbolCode.Exclamation,
            Comment = "Open"
        };

        PacketDetailItemViewModel packet = new(record, portName: null);

        Assert.Equal("SHELTER1", ValueOf(packet, "ObjectName"));
        Assert.Equal(Strings.Get("KilledObjectBadge"), ValueOf(packet, "PacketDetailLabelStatus"));
        Assert.Contains(
            packet.DecodedFields,
            field => field.Label == Strings.Get("Location") && field.Value.Contains("38.5000", StringComparison.Ordinal));
        Assert.Equal(
            Strings.Format("PacketDetailMetersFormat", 120),
            ValueOf(packet, "PacketDetailLabelAltitude"));
        Assert.Equal("Open", ValueOf(packet, "Comment"));
    }

    [Fact]
    public void Bulletin_ReportsItsNameAndText()
    {
        PacketRecord record = CreatePacket();
        record.PacketType = "Bulletin";
        record.Bulletin = new BulletinDataRecord
        {
            Name = "BLN0TEST",
            Text = "Net tonight",
            Number = 7
        };

        PacketDetailItemViewModel packet = new(record, portName: null);

        Assert.Equal("BLN0TEST", ValueOf(packet, "BulletinName"));
        Assert.Equal("Net tonight", ValueOf(packet, "BulletinText"));
        Assert.Equal("7", ValueOf(packet, "PacketDetailNumber"));
    }

    [Fact]
    public void UnknownFrame_StillShowsTheEnvelopeAndSaysWhatIsMissing()
    {
        PacketRecord record = CreatePacket();
        record.PacketType = "Unknown";
        record.RawInfo = "\u0001unparseable";

        PacketDetailItemViewModel packet = new(record, portName: null);

        Assert.True(packet.IsUndecoded);
        Assert.Equal(Strings.Get("PacketDetailUnknownFrame"), packet.PacketTypeText);
        Assert.Equal(Strings.Get("PacketDetailUndecoded"), packet.UndecodedText);
        // The raw line keeps the path, so the frame can be matched against a log.
        Assert.Equal($"{record.RawPath}:{record.RawInfo}", packet.RawLine);
        // The envelope is still described: direction, port, path and receipt time.
        Assert.Equal(4, packet.Provenance.Count);
    }

    [Fact]
    public void DecodedFrame_HasNoUndecodedNotice()
    {
        PacketRecord record = CreatePacket();
        record.Position = new PositionDataRecord { Location = new Coordinate(38.5, -9.0) };

        PacketDetailItemViewModel packet = new(record, portName: null);

        Assert.False(packet.IsUndecoded);
        Assert.Empty(packet.UndecodedText);
    }
}
