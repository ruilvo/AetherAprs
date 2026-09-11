// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Globalization;
using System.Text;
using AetherAprs.Models.Aprs;

namespace AetherAprs.Modems.Aprs;

/// <summary>
/// Static serializer for APRS packets. Produces the APRS info field string
/// (the application-layer payload) from a typed <see cref="AprsPacket"/>.
/// </summary>
/// <remarks>
/// This handles only the APRS info field format (e.g. <c>!DDMM.mmX/DDDMM.mmYC...</c>).
/// For AX.25 framing (addresses + control + PID), use <see cref="Ax25Serializer"/>.
/// </remarks>
public static class AprsSerializer
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
            MessagePacket msg => FormatMessage(msg),
            StatusPacket status => FormatStatus(status),
            WeatherPacket weather => FormatWeather(weather),
            UnknownPacket unknown => unknown.Raw,
            _ => throw new ArgumentException($"Unsupported packet type: {packet.GetType().Name}.")
        };
    }

    // ---------------------------------------------------------------
    // Position formatter
    // ---------------------------------------------------------------

    private static string FormatPosition(PositionPacket packet)
    {
        // Format: !DDMM.mmX[overlay]/DDDMM.mmYC[comment]
        // (using '!' — no timestamp, suitable for typical fixed/generic positions)
        // The '/' or '\' between lat and lon IS the symbol table indicator.
        char typeId = '!';

        string lat = FormatLatitude(packet.Latitude, packet.Precision);
        string lon = FormatLongitude(packet.Longitude, packet.Precision);

        var info = new StringBuilder();
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

    private static string FormatLatitude(double latitude, int precision)
    {
        ValidateCoordinate(latitude, -90, 90, nameof(latitude));
        ValidatePrecision(precision);

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

    private static string FormatLongitude(double longitude, int precision)
    {
        ValidateCoordinate(longitude, -180, 180, nameof(longitude));
        ValidatePrecision(precision);

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

    private static void ValidateCoordinate(double coordinate, double minimum, double maximum, string parameterName)
    {
        if (!double.IsFinite(coordinate) || coordinate < minimum || coordinate > maximum)
        {
            throw new ArgumentOutOfRangeException(parameterName, coordinate, $"Coordinate must be between {minimum} and {maximum} degrees.");
        }
    }

    private static void ValidatePrecision(int precision)
    {
        if (precision is < 1 or > 2)
        {
            throw new ArgumentOutOfRangeException(nameof(precision), precision, "APRS uncompressed positions require one or two decimal minute digits.");
        }
    }

    // ---------------------------------------------------------------
    // Message formatter
    // ---------------------------------------------------------------

    private static string FormatMessage(MessagePacket packet)
    {
        // Format: :ADDRESSEE :message text{msgid}
        var info = new StringBuilder();
        info.Append(':');
        info.Append(packet.Addressee.Base.PadRight(9, ' '));
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

    // ---------------------------------------------------------------
    // Status formatter
    // ---------------------------------------------------------------

    private static string FormatStatus(StatusPacket packet)
    {
        // Format: >status text
        return $">{packet.Text}";
    }

    // ---------------------------------------------------------------
    // Weather formatter
    // ---------------------------------------------------------------

    private static string FormatWeather(WeatherPacket packet)
    {
        // Format: _c111s222g333t444h55b77777r111p222...
        // Only non-null fields are included.
        var info = new StringBuilder();
        info.Append('_');

        if (packet.WindDirection.HasValue)
            info.Append(CultureInfo.InvariantCulture, $"c{packet.WindDirection.Value:F0}");
        if (packet.WindSpeed.HasValue)
            info.Append(CultureInfo.InvariantCulture, $"s{packet.WindSpeed.Value:F0}");
        if (packet.WindGust.HasValue)
            info.Append(CultureInfo.InvariantCulture, $"g{packet.WindGust.Value:F0}");
        if (packet.Temperature.HasValue)
            info.Append(CultureInfo.InvariantCulture, $"t{packet.Temperature.Value:F0}");
        if (packet.Humidity.HasValue)
            info.Append(CultureInfo.InvariantCulture, $"h{packet.Humidity.Value:F0}");
        if (packet.Pressure.HasValue)
            info.Append(CultureInfo.InvariantCulture, $"b{packet.Pressure.Value:F0}");
        if (packet.Rain1h.HasValue)
            info.Append(CultureInfo.InvariantCulture, $"r{packet.Rain1h.Value:F2}");
        if (packet.Rain24h.HasValue)
            info.Append(CultureInfo.InvariantCulture, $"p{packet.Rain24h.Value:F2}");

        return info.ToString();
    }
}
