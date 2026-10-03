// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AetherAprs.Models.Aprs;

/// <summary>
/// APRS station identifier suffix and its conventional use.
/// </summary>
[JsonConverter(typeof(AprsSsidJsonConverter))]
public enum AprsSsid : byte
{
    /// <summary>Primary fixed station, usually message capable.</summary>
    PrimaryStation = 0,
    /// <summary>Generic additional station, digipeater, mobile, or weather station.</summary>
    AdditionalStation1 = 1,
    /// <summary>Generic additional station, digipeater, mobile, or weather station.</summary>
    AdditionalStation2 = 2,
    /// <summary>Generic additional station, digipeater, mobile, or weather station.</summary>
    AdditionalStation3 = 3,
    /// <summary>Generic additional station, digipeater, mobile, or weather station.</summary>
    AdditionalStation4 = 4,
    /// <summary>Other networks such as D-STAR, iPhone, Android, or BlackBerry.</summary>
    OtherNetworks = 5,
    /// <summary>Special activity, satellite operations, camping, or 6 meters.</summary>
    SpecialActivity = 6,
    /// <summary>Walkie-talkies, handheld radios, or other human-portable stations.</summary>
    HumanPortable = 7,
    /// <summary>Boats, sailboats, RVs, or a second main mobile.</summary>
    SecondaryMobile = 8,
    /// <summary>Primary mobile station, usually message capable.</summary>
    PrimaryMobile = 9,
    /// <summary>Internet, IGates, EchoLink, Winlink, AVRS, or APRN.</summary>
    InternetGateway = 10,
    /// <summary>Balloons, aircraft, or spacecraft.</summary>
    Aircraft = 11,
    /// <summary>APRStt, DTMF, RFID, devices, or one-way trackers.</summary>
    Devices = 12,
    /// <summary>Weather station.</summary>
    WeatherStation = 13,
    /// <summary>Truckers or generally full-time drivers.</summary>
    Trucker = 14,
    /// <summary>Generic additional station, digipeater, mobile, or weather station.</summary>
    AdditionalStation15 = 15
}

/// <summary>
/// Converts <see cref="AprsSsid"/> values to and from numeric JSON values.
/// </summary>
public sealed class AprsSsidJsonConverter : JsonConverter<AprsSsid>
{
    public override AprsSsid Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.Number
            || !reader.TryGetByte(out var value)
            || value > 15)
        {
            throw new JsonException("APRS SSID must be a number between 0 and 15.");
        }

        return (AprsSsid)value;
    }

    public override void Write(
        Utf8JsonWriter writer,
        AprsSsid value,
        JsonSerializerOptions options)
    {
        if (!Enum.IsDefined(value))
        {
            throw new JsonException($"Invalid APRS SSID value: {value}.");
        }

        writer.WriteNumberValue((byte)value);
    }
}
