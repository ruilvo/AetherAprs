// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Data.Entities;
using AetherAprs.Localization;
using AetherAprs.Models.Aprs;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace AetherAprs.ViewModels.Components;

/// <summary>
/// A single packet as the details page presents it: when it was received, when the
/// station says it was sent, where it came from, and every field the decoder could
/// recover from it.
/// </summary>
/// <remarks>
/// Presentation strings are built here rather than in AXAML so the labels and unit
/// formats stay localized, and so an unknown frame still gets a readable summary
/// instead of an empty card.
/// </remarks>
public sealed class PacketDetailItemViewModel
{
    private const string TimestampFormat = "yyyy-MM-dd HH:mm:ss";

    private readonly List<PacketDetailField> _provenance = [];
    private readonly List<PacketDetailField> _decodedFields = [];

    /// <summary>
    /// Wraps a stored packet for display.
    /// </summary>
    /// <param name="packet">The stored packet.</param>
    /// <param name="portName">The name of the port the packet arrived on, if known.</param>
    public PacketDetailItemViewModel(PacketRecord packet, string? portName)
    {
        ArgumentNullException.ThrowIfNull(packet);

        PacketId = packet.Id;
        ReceivedAt = ToLocal(packet.Timestamp);
        ProtocolTime = packet.PacketTimestamp is { } protocolTime ? ToLocal(protocolTime) : null;
        PacketTypeText = ResolveTypeText(packet.PacketType);
        RawLine = string.IsNullOrEmpty(packet.RawPath)
            ? packet.RawInfo
            : $"{packet.RawPath}:{packet.RawInfo}";
        RawFrameHex = packet.RawFrameHex;

        BuildProvenance(packet, portName);
        BuildDecodedDetail(packet);
    }

    /// <summary>
    /// The stored row identity, so callers can correlate a presented row with the
    /// paging order without holding on to the entity.
    /// </summary>
    public long PacketId { get; }

    /// <summary>Local time at which the packet reached this station.</summary>
    public DateTimeOffset ReceivedAt { get; }

    /// <summary>
    /// Local time carried inside the packet itself, or <see langword="null"/> when the
    /// format does not carry one. Never conflated with <see cref="ReceivedAt"/>.
    /// </summary>
    public DateTimeOffset? ProtocolTime { get; }

    public bool HasProtocolTime => ProtocolTime is not null;

    public string ReceivedAtText => ReceivedAt.ToString(TimestampFormat, CultureInfo.CurrentCulture);

    public string ProtocolTimeText => ProtocolTime?.ToString(TimestampFormat, CultureInfo.CurrentCulture) ?? string.Empty;

    /// <summary>The decoded packet type, localized.</summary>
    public string PacketTypeText { get; }

    /// <summary>
    /// Where the packet came from, as one readable line: direction, port, path and
    /// times. Kept as fields so the page renders it without per-row logic.
    /// </summary>
    public IReadOnlyList<PacketDetailField> Provenance => _provenance;

    /// <summary>
    /// The fields decoded from the packet's own information field, by packet type.
    /// Empty when the frame could not be decoded.
    /// </summary>
    public IReadOnlyList<PacketDetailField> DecodedFields => _decodedFields;

    public bool HasDecodedFields => _decodedFields.Count > 0;

    /// <summary>The reconstructed APRS line, path and information field.</summary>
    public string RawLine { get; }

    /// <summary>The AX.25 frame in hex, for packets that arrived over RF.</summary>
    public string RawFrameHex { get; }

    public bool HasRawFrameHex => !string.IsNullOrEmpty(RawFrameHex);

    /// <summary>
    /// True when nothing beyond the envelope could be decoded, so the page can say so
    /// instead of showing a blank card.
    /// </summary>
    public bool IsUndecoded => !HasDecodedFields;

    /// <summary>
    /// Explanation shown when the frame decoded to nothing recognizable. It names the
    /// information field so an operator can still identify the station's own format.
    /// </summary>
    public string UndecodedText =>
        IsUndecoded ? Strings.Get("PacketDetailUndecoded") : string.Empty;

    private static string FormatValue(string formatKey, double value) =>
        Strings.Format(formatKey, value);

    /// <summary>One labeled row, with the label already localized.</summary>
    private static PacketDetailField Field(string label, string value) => new(label, value);

    private static DateTimeOffset ToLocal(DateTime value)
    {
        // SQLite round-trips DateTime without a Kind and the data layer stores UTC, so
        // an Unspecified value is UTC rather than local time.
        DateTime utc = value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };

        return new DateTimeOffset(utc, TimeSpan.Zero).ToLocalTime();
    }

    private void BuildProvenance(PacketRecord packet, string? portName)
    {
        _provenance.Add(Field(Strings.Get("PacketDetailDirection"), Strings.Get(
            packet.IsOutbound ? "PacketDetailSent" : "PacketDetailReceived")));
        _provenance.Add(Field(
            Strings.Get("Port"),
            string.IsNullOrWhiteSpace(portName) ? Strings.Get("PacketDetailPortUnknown") : portName));

        // The path is stored exactly as received, so it is shown verbatim: a repeated
        // digipeater stays recognisable by its trailing '*'.
        if (!string.IsNullOrEmpty(packet.RawPath))
        {
            _provenance.Add(Field(Strings.Get("PacketDetailPath"), packet.RawPath));
        }

        _provenance.Add(Field(Strings.Get("PacketDetailReceivedAt"), ReceivedAtText));
        if (ProtocolTime is { } protocolTime)
        {
            _provenance.Add(Field(
                Strings.Get("PacketDetailProtocolTime"),
                protocolTime.ToString(TimestampFormat, CultureInfo.CurrentCulture)));
        }
    }

    private void BuildDecodedDetail(PacketRecord packet)
    {
        if (packet.Position is { } position)
        {
            AddPosition(position.Location, position.Altitude, position.Course, position.Speed, position.Comment);
            _decodedFields.Add(Field(
                Strings.Get("ObjectSymbol"),
                $"{position.SymbolTable.ToChar()}{position.SymbolCode.ToChar()}"));
        }

        if (packet.Object is { } obj)
        {
            _decodedFields.Add(Field(Strings.Get("ObjectName"), obj.Name));
            _decodedFields.Add(Field(
                Strings.Get("PacketDetailLabelStatus"),
                Strings.Get(obj.IsLive ? "PacketDetailLive" : "KilledObjectBadge")));
            AddPosition(obj.Location, obj.Altitude, obj.Course, obj.Speed, obj.Comment);
            _decodedFields.Add(Field(
                Strings.Get("ObjectSymbol"),
                $"{obj.SymbolTable.ToChar()}{obj.SymbolCode.ToChar()}"));
        }

        if (packet.Item is { } item)
        {
            _decodedFields.Add(Field(Strings.Get("ObjectName"), item.Name));
            _decodedFields.Add(Field(
                Strings.Get("PacketDetailLabelStatus"),
                Strings.Get(item.IsLive ? "PacketDetailLive" : "KilledObjectBadge")));
            AddPosition(item.Location, item.Altitude, item.Course, item.Speed, item.Comment);
            _decodedFields.Add(Field(
                Strings.Get("ObjectSymbol"),
                $"{item.SymbolTable.ToChar()}{item.SymbolCode.ToChar()}"));
        }

        if (packet.Message is { } message)
        {
            _decodedFields.Add(Field(
                Strings.Get("PacketDetailAddressee"),
                AprsSsidExtensions.FormatCallsign(message.AddresseeBase, message.AddresseeSsid)));
            _decodedFields.Add(Field(Strings.Get("Message"), message.Text));
            if (message.Number is { } number)
            {
                _decodedFields.Add(Field(
                    Strings.Get("PacketDetailNumber"),
                    number.ToString(CultureInfo.CurrentCulture)));
            }
        }

        if (packet.Status is { } status)
        {
            _decodedFields.Add(Field(Strings.Get("PacketDetailLabelStatus"), status.Text));
        }

        if (packet.Weather is { } weather)
        {
            AddIfPresent("Temperature", "PacketDetailTemperatureFormat", weather.Temperature);
            AddIfPresent("WindSpeed", "PacketDetailWindSpeedFormat", weather.WindSpeed);
            AddIfPresent("WindDirection", "PacketDetailWindDirectionFormat", weather.WindDirection);
            AddIfPresent("Humidity", "PacketDetailHumidityFormat", weather.Humidity);
            AddIfPresent("Pressure", "PacketDetailPressureFormat", weather.Pressure);
            AddIfPresent("RainLastHour", "PacketDetailInchesFormat", weather.RainLastHour);
            AddIfPresent("RainLast24Hours", "PacketDetailInchesFormat", weather.RainLast24Hours);
        }

        if (packet.Telemetry is { } telemetry)
        {
            _decodedFields.Add(Field(
                Strings.Get("TelemetrySequence"),
                telemetry.SequenceNumber.ToString(CultureInfo.CurrentCulture)));

            AddAnalog("TelemetryAnalog1", telemetry.Analog1);
            AddAnalog("TelemetryAnalog2", telemetry.Analog2);
            AddAnalog("TelemetryAnalog3", telemetry.Analog3);
            AddAnalog("TelemetryAnalog4", telemetry.Analog4);
            AddAnalog("TelemetryAnalog5", telemetry.Analog5);

            if (telemetry.DigitalValue is { } digital)
            {
                _decodedFields.Add(Field(
                    Strings.Get("TelemetryDigital"),
                    digital.ToString(CultureInfo.CurrentCulture)));
            }
        }

        if (packet.Capabilities is { } capabilities)
        {
            _decodedFields.Add(Field(Strings.Get("Capabilities"), capabilities.Text));
        }

        if (packet.Bulletin is { } bulletin)
        {
            _decodedFields.Add(Field(Strings.Get("BulletinName"), bulletin.Name));
            _decodedFields.Add(Field(Strings.Get("BulletinText"), bulletin.Text));
            if (bulletin.Number is { } bulletinNumber)
            {
                _decodedFields.Add(Field(
                    Strings.Get("PacketDetailNumber"),
                    bulletinNumber.ToString(CultureInfo.CurrentCulture)));
            }
        }
    }

    private void AddPosition(
        Geo.Coordinate location,
        double? altitude,
        double? course,
        double? speed,
        string? comment)
    {
        _decodedFields.Add(Field(Strings.Get("Location"), Strings.Format(
            "PreviewLatLon",
            location.Latitude.ToString("F4", CultureInfo.InvariantCulture),
            location.Longitude.ToString("F4", CultureInfo.InvariantCulture))));

        if (altitude is { } altitudeValue)
        {
            _decodedFields.Add(Field(
                Strings.Get("PacketDetailLabelAltitude"),
                FormatValue("PacketDetailMetersFormat", altitudeValue)));
        }

        if (course is { } courseValue)
        {
            _decodedFields.Add(Field(
                Strings.Get("PacketDetailLabelCourse"),
                FormatValue("PacketDetailDegreesFormat", courseValue)));
        }

        if (speed is { } speedValue)
        {
            _decodedFields.Add(Field(
                Strings.Get("PacketDetailLabelSpeed"),
                FormatValue("PacketDetailKnotsFormat", speedValue)));
        }

        if (!string.IsNullOrEmpty(comment))
        {
            _decodedFields.Add(Field(Strings.Get("Comment"), comment));
        }
    }

    private void AddIfPresent(string labelKey, string formatKey, double? value)
    {
        if (value is { } present)
        {
            _decodedFields.Add(Field(Strings.Get(labelKey), FormatValue(formatKey, present)));
        }
    }

    private void AddAnalog(string labelKey, double? value) =>
        AddIfPresent(labelKey, "PacketDetailScalarFormat", value);

    private static string ResolveTypeText(string packetType) => packetType switch
    {
        // The stored values are the discriminator strings written by the packet
        // record mapper; they are matched here rather than referenced because the
        // mapper's constants are an internal persistence detail.
        "Position" => Strings.Get("PacketTypePosition"),
        "Message" or "MessageAck" or "MessageRej" or "Bulletin" => Strings.Get("PacketTypeMessage"),
        "Status" => Strings.Get("PacketTypeStatus"),
        "Weather" => Strings.Get("PacketTypeWeather"),
        "Telemetry" => Strings.Get("PacketTypeTelemetry"),
        "Capabilities" => Strings.Get("PacketTypeUserDefined"),
        "Object" => Strings.Get("PacketTypeObject"),
        "Item" => Strings.Get("PacketTypeItem"),
        "Unknown" or "" => Strings.Get("PacketDetailUnknownFrame"),
        _ => packetType
    };
}

/// <summary>One label/value row in the packet details page.</summary>
/// <param name="Label">The localized field name.</param>
/// <param name="Value">The decoded value.</param>
public sealed record PacketDetailField(string Label, string Value)
{
    /// <summary>True when the value is present, so empty rows stay hidden.</summary>
    public bool HasValue => !string.IsNullOrWhiteSpace(Value);
}
