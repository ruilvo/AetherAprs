// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Models.Aprs;
using AetherAprs.Models.Aprs.Packets;
using System;
using System.Globalization;
using System.Text;

namespace AetherAprs.Modems.Aprs;

/// <summary>
/// Static serializer for APRS packets. Produces the APRS info field string
/// (the application-layer payload) from a typed <see cref="AprsPacket"/>.
/// </summary>
/// <remarks>
/// This handles only the APRS info field format (e.g. <c>!DDMM.mmX/DDDMM.mmYC...</c>).
/// For AX.25 framing (addresses + control + PID), use <see cref="Ax25Serializer"/>.
/// </remarks>
public static class AprsInfoFieldSerializer
{
    /// <summary>
    /// Formats an <see cref="AprsPacket"/> into its APRS info field string.
    /// </summary>
    /// <param name="packet">The packet to format.</param>
    /// <returns>The APRS info field string (e.g. <c>!3830.00N/00906.00E#</c>).</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="packet"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown if the packet type is not supported.</exception>
    public static string FormatInfoField(AprsPacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);

        return packet switch
        {
            PositionPacket pos => FormatPosition(pos),
            ObjectPacket obj => FormatObject(obj),
            ItemPacket item => FormatItem(item),
            MessagePacket msg => FormatMessage(msg),
            BulletinPacket bulletin => FormatBulletin(bulletin),
            MessageAckPacket ack => FormatMessageAckRej(ack.Addressee, "ack", ack.MessageNumber),
            MessageRejPacket rej => FormatMessageAckRej(rej.Addressee, "rej", rej.MessageNumber),
            StatusPacket status => FormatStatus(status),
            WeatherPacket weather => FormatWeather(weather),
            ThirdPartyPacket thirdParty => "}" + thirdParty.Encapsulated,
            QueryPacket query => "?" + query.Query,
            RawGpsPacket rawGps => "$" + rawGps.Sentence,
            UnknownPacket unknown => unknown.Raw,
            _ => throw new ArgumentException($"Unsupported packet type: {packet.GetType().Name}.")
        };
    }

    /// <summary>
    /// Formats a <see cref="PositionPacket"/> into its APRS info field string.
    /// </summary>
    /// <param name="packet">The position packet to format.</param>
    /// <returns>The APRS info field string.</returns>
    private static string FormatPosition(PositionPacket packet) => packet.Format switch
    {
        PositionFormat.Compressed => FormatPositionCompressed(packet),
        PositionFormat.MicE => MicESerializer.FormatInfoField(packet),
        _ => FormatPositionUncompressed(packet)
    };

    /// <summary>
    /// Formats an uncompressed <see cref="PositionPacketBase"/> into its APRS info field string.
    /// </summary>
    /// <param name="packet">The position-bearing packet to format.</param>
    /// <returns>The APRS info field string.</returns>
    private static string FormatPositionUncompressed(PositionPacketBase packet)
    {
        // Format: !DDMM.mmX[overlay]/DDDMM.mmYC[comment]
        // (using '!' — no timestamp, suitable for typical fixed/generic positions)
        // The '/' or '\' between lat and lon IS the symbol table indicator.
        char typeId = '!';

        string lat = FormatLatitude(packet.Location.Latitude, packet.Precision);
        string lon = FormatLongitude(packet.Location.Longitude, packet.Precision);

        StringBuilder info = new();
        info.Append(typeId);
        info.Append(lat);

        if (packet.Symbol.Overlay.HasValue)
        {
            info.Append(packet.Symbol.Overlay.Value);
        }

        info.Append(packet.Symbol.TableChar); // separator between lat and lon = symbol table
        info.Append(lon);
        info.Append(packet.Symbol.CodeChar);

        if (!string.IsNullOrEmpty(packet.Comment))
        {
            info.Append(packet.Comment);
        }

        return info.ToString();
    }

    /// <summary>
    /// Formats a <see cref="PositionPacket"/> as an APRS compressed (base-91) position.
    /// </summary>
    /// <param name="packet">The position packet to format.</param>
    /// <returns>The APRS info field string.</returns>
    private static string FormatPositionCompressed(PositionPacket packet)
    {
        // Compressed position format (aprslib-compatible):
        //   ! + [table|overlay] + 4 base-91 lat + 4 base-91 lon + symbol + csT[ + comment]
        // Latitude is encoded as 90 - latN/380926, longitude as -180 + lonN/190463
        // (inverse of the parser's TryParseCompressedPosition decode).

        int latN = (int)Math.Round((90.0 - packet.Location.Latitude) * 380926.0, MidpointRounding.AwayFromZero);
        int lonN = (int)Math.Round((packet.Location.Longitude + 180.0) * 190463.0, MidpointRounding.AwayFromZero);

        StringBuilder info = new();
        info.Append('!');

        if (packet.Symbol.Overlay.HasValue)
        {
            info.Append(packet.Symbol.Overlay.Value);
        }
        else
        {
            info.Append(packet.Symbol.TableChar);
        }

        info.Append(EncodeBase91(latN, 4));
        info.Append(EncodeBase91(lonN, 4));
        info.Append(packet.Symbol.CodeChar);
        info.Append(FormatCompressedCsT(packet));

        if (!string.IsNullOrEmpty(packet.Comment))
        {
            info.Append(packet.Comment);
        }

        return info.ToString();
    }

    /// <summary>
    /// Formats the compressed-position course/speed/type (csT) triple. When no
    /// course/speed/altitude is available, three spaces are emitted (the parser
    /// treats a space as "absent").
    /// </summary>
    private static string FormatCompressedCsT(PositionPacket packet)
    {
        if (packet.Altitude is > 0)
        {
            // Altitude mode: c1*91 + s1 encodes log1.002(altitude in feet).
            (int c1Value, int s1Value) = AprsUnits.EncodeCompressedAltitude(packet.Altitude.Value);
            char c1 = (char)(c1Value + 33);
            char s1 = (char)(s1Value + 33);
            char ctype = (char)(0x10 + 33); // altitude flag set (bit 4)
            return $"{c1}{s1}{ctype}";
        }

        if (packet.Course.HasValue && packet.Speed.HasValue)
        {
            // Course in units of 4 degrees (c1==0 means 360), speed as 1.08^s1 - 1 knots.
            int course = (int)Math.Round(packet.Course.Value, MidpointRounding.AwayFromZero);
            int c1 = course >= 360 ? 0 : (int)Math.Round(course / 4.0, MidpointRounding.AwayFromZero);
            int s1 = AprsUnits.EncodeCompressedSpeed(packet.Speed.Value);
            char c = (char)(c1 + 33);
            char s = (char)(s1 + 33);
            char ctype = (char)33; // no altitude flag
            return $"{c}{s}{ctype}";
        }

        return "   ";
    }

    /// <summary>
    /// Encodes an integer value as a fixed number of base-91 digits (most
    /// significant digit first), each mapped to the printable range '!' (33) .. '{' (123).
    /// </summary>
    private static string EncodeBase91(int value, int digits)
    {
        char[] chars = new char[digits];
        for (int i = digits - 1; i >= 0; i--)
        {
            chars[i] = (char)(value % 91 + 33);
            value /= 91;
        }

        return new string(chars);
    }

    /// <summary>
    /// Formats a latitude value into APRS compressed format (DDMM.mmN/S).
    /// </summary>
    /// <param name="latitude">The latitude value to format.</param>
    /// <param name="precision">The precision of the latitude value.</param>
    /// <returns>The formatted latitude value.</returns>
    private static string FormatLatitude(double latitude, int precision)
    {
        ValidateCoordinate(latitude, -90, 90, nameof(latitude));
        precision = NormalizePrecision(precision);

        bool isSouth = latitude < 0;
        double absLat = Math.Abs(latitude);

        int deg = (int)absLat;
        double minDecimal = (absLat - deg) * 60.0;
        int scale = (int)Math.Pow(10, precision);
        int roundedMinutes = (int)Math.Round(minDecimal * scale, MidpointRounding.AwayFromZero);
        int min = roundedMinutes / scale;
        int decValue = roundedMinutes % scale;

        if (min == 60)
        {
            deg++;
            min = 0;
        }

        char dir = isSouth ? 'S' : 'N';

        return $"{deg:D2}{min:D2}.{decValue.ToString(new string('0', precision))}{dir}";
    }

    /// <summary>
    /// Formats a longitude value into APRS compressed format (DDDMM.mmE/W).
    /// </summary>
    /// <param name="longitude">The longitude value to format.</param>
    /// <param name="precision">The precision of the longitude value.</param>
    /// <returns>The formatted longitude value.</returns>
    private static string FormatLongitude(double longitude, int precision)
    {
        ValidateCoordinate(longitude, -180, 180, nameof(longitude));
        precision = NormalizePrecision(precision);

        bool isWest = longitude < 0;
        double absLon = Math.Abs(longitude);

        int deg = (int)absLon;
        double minDecimal = (absLon - deg) * 60.0;
        int scale = (int)Math.Pow(10, precision);
        int roundedMinutes = (int)Math.Round(minDecimal * scale, MidpointRounding.AwayFromZero);
        int min = roundedMinutes / scale;
        int decValue = roundedMinutes % scale;

        if (min == 60)
        {
            deg++;
            min = 0;
        }

        char dir = isWest ? 'W' : 'E';

        return $"{deg:D3}{min:D2}.{decValue.ToString(new string('0', precision))}{dir}";
    }

    /// <summary>
    /// Validates that a coordinate value is within the specified range and is finite.
    /// </summary>
    /// <param name="coordinate">The coordinate value to validate.</param>
    /// <param name="minimum">The minimum allowed value.</param>
    /// <param name="maximum">The maximum allowed value.</param>
    /// <param name="parameterName">The name of the parameter being validated.</param>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    private static void ValidateCoordinate(double coordinate, double minimum, double maximum, string parameterName)
    {
        if (!double.IsFinite(coordinate) || coordinate < minimum || coordinate > maximum)
        {
            throw new ArgumentOutOfRangeException(parameterName, coordinate, $"Coordinate must be between {minimum} and {maximum} degrees.");
        }
    }

    /// <summary>
    /// Normalizes the precision value to 1 or 2 for APRS uncompressed positions.
    /// Compressed positions may use precision 3, which is clamped to 2.
    /// </summary>
    /// <param name="precision">The precision value to normalize.</param>
    /// <returns>Normalized precision (1 or 2).</returns>
    private static int NormalizePrecision(int precision) =>
        // Clamp to valid range: compressed positions set precision=3, normalize to 2
        Math.Clamp(precision, 1, 2);

    /// <summary>
    /// Formats a <see cref="MessagePacket"/> into its APRS info field string.
    /// </summary>
    /// <param name="packet">The message packet to format.</param>
    /// <returns>The formatted APRS info field string.</returns>
    private static string FormatMessage(MessagePacket packet)
    {
        // Format: :ADDRESSEE :message text{msgid}
        StringBuilder info = new();
        info.Append(':');
        info.Append(packet.Addressee.ToString().PadRight(9, ' '));
        info.Append(':');

        if (packet.MessageNumber.HasValue)
        {
            info.Append($"{packet.Text}{{{packet.MessageNumber.Value}}}");
        }
        else
        {
            info.Append(packet.Text);
        }

        return info.ToString();
    }

    /// <summary>
    /// Formats an <see cref="ObjectPacket"/> into its APRS info field string.
    /// </summary>
    /// <param name="packet">The object packet to format.</param>
    /// <returns>The formatted APRS info field string.</returns>
    private static string FormatObject(ObjectPacket packet)
    {
        // Format: ;NAME(9)*DDHHMMz<position> (or '_' for a killed object).
        StringBuilder info = new();
        info.Append(';');
        info.Append(packet.Name.PadRight(9, ' '));
        info.Append(packet.IsLive ? '*' : '_');
        info.Append(string.IsNullOrEmpty(packet.RawTimestamp) ? "000000z" : packet.RawTimestamp);
        info.Append(FormatPositionUncompressed(packet)[1..]);
        return info.ToString();
    }

    /// <summary>
    /// Formats an <see cref="ItemPacket"/> into its APRS info field string.
    /// </summary>
    /// <param name="packet">The item packet to format.</param>
    /// <returns>The formatted APRS info field string.</returns>
    private static string FormatItem(ItemPacket packet)
    {
        // Format: )NAME(3-9)!<position> (or '_' for a killed item).
        StringBuilder info = new();
        info.Append(')');
        info.Append(packet.Name);
        info.Append(packet.IsLive ? '!' : '_');
        info.Append(FormatPositionUncompressed(packet)[1..]);
        return info.ToString();
    }

    /// <summary>
    /// Formats a <see cref="BulletinPacket"/> into its APRS info field string.
    /// </summary>
    /// <param name="packet">The bulletin packet to format.</param>
    /// <returns>The formatted APRS info field string.</returns>
    private static string FormatBulletin(BulletinPacket packet)
    {
        // Format: :NAME(9):text{msgid}
        StringBuilder info = new();
        info.Append(':');
        info.Append(packet.Name.PadRight(9, ' '));
        info.Append(':');

        if (packet.MessageNumber.HasValue)
        {
            info.Append($"{packet.Text}{{{packet.MessageNumber.Value}}}");
        }
        else
        {
            info.Append(packet.Text);
        }

        return info.ToString();
    }

    /// <summary>
    /// Formats a message acknowledgment or rejection into its APRS info field string.
    /// </summary>
    /// <param name="addressee">The addressee being acknowledged/rejected.</param>
    /// <param name="keyword">Either "ack" or "rej".</param>
    /// <param name="messageNumber">The message number being acknowledged/rejected.</param>
    /// <returns>The formatted APRS info field string.</returns>
    private static string FormatMessageAckRej(AprsAddress addressee, string keyword, int messageNumber)
    {
        // Format: :ADDRESSEE :ack{N} / :ADDRESSEE :rej{N}
        StringBuilder info = new();
        info.Append(':');
        info.Append(addressee.ToString().PadRight(9, ' '));
        info.Append(':');
        info.Append(CultureInfo.InvariantCulture, $"{keyword}{messageNumber}");
        return info.ToString();
    }

    /// <summary>
    /// Formats a <see cref="StatusPacket"/> into its APRS info field string.
    /// </summary>
    /// <param name="packet">The status packet to format.</param>
    /// <returns>The formatted APRS info field string.</returns>
    private static string FormatStatus(StatusPacket packet) =>
        // Format: >status text
        $">{packet.Text}";

    /// <summary>
    /// Formats a <see cref="WeatherPacket"/> into its APRS info field string.
    /// </summary>
    /// <param name="packet">The weather packet to format.</param>
    /// <returns>The formatted APRS info field string.</returns>
    private static string FormatWeather(WeatherPacket packet)
    {
        // Format: _c111s222g333t444h55b77777r111p222...
        // Per the APRS spec all fields are fixed-width integers (no decimal
        // point); rain amounts are in hundredths of an inch. Only non-null
        // fields are included.
        StringBuilder info = new();
        info.Append('_');

        if (packet.WindDirection.HasValue)
        {
            info.Append(CultureInfo.InvariantCulture, $"c{FormatFixedWidth(packet.WindDirection.Value, 3)}");
        }

        if (packet.WindSpeed.HasValue)
        {
            info.Append(CultureInfo.InvariantCulture, $"s{FormatFixedWidth(packet.WindSpeed.Value, 3)}");
        }

        if (packet.WindGust.HasValue)
        {
            info.Append(CultureInfo.InvariantCulture, $"g{FormatFixedWidth(packet.WindGust.Value, 3)}");
        }

        if (packet.Temperature.HasValue)
        {
            // Per the APRS weather spec, sub-zero temperatures are encoded as
            // 1000 - abs(temp): -5 °F transmits as t995.
            double temperature = packet.Temperature.Value;
            double encoded = temperature < 0 ? 1000 - Math.Abs((int)temperature) : temperature;
            info.Append(CultureInfo.InvariantCulture, $"t{FormatFixedWidth(encoded, 3)}");
        }

        if (packet.Humidity.HasValue)
        {
            info.Append(CultureInfo.InvariantCulture, $"h{FormatFixedWidth(packet.Humidity.Value, 2)}");
        }

        if (packet.Pressure.HasValue)
        {
            // Barometric pressure in tenths of millibars (5 digits).
            info.Append(CultureInfo.InvariantCulture, $"b{FormatFixedWidth(packet.Pressure.Value * 10, 5)}");
        }

        if (packet.Rain1h.HasValue)
        {
            info.Append(CultureInfo.InvariantCulture, $"r{FormatFixedWidth(packet.Rain1h.Value * 100, 3)}");
        }

        if (packet.Rain24h.HasValue)
        {
            info.Append(CultureInfo.InvariantCulture, $"p{FormatFixedWidth(packet.Rain24h.Value * 100, 3)}");
        }

        return info.ToString();
    }

    /// <summary>
    /// Formats a value as a zero-padded fixed-width integer per the APRS
    /// fixed-width weather field convention.
    /// </summary>
    private static string FormatFixedWidth(double value, int width) => Math.Round(value).ToString(CultureInfo.InvariantCulture).PadLeft(width, '0');
}
