// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace AetherAprs.Models.Aprs;

/// <summary>
/// Models an APRS-IS server-side filter string and can round-trip between the
/// structured form (used by the filter builder UI) and the space-separated
/// string sent to the server (see the javAPRSSrvr filter grammar).
///
/// Supported directives:
/// <list type="bullet">
/// <item><c>r/lat/lon/dist</c> — range filter (circle around a point)</item>
/// <item><c>p/aa/bb</c> — pass prefixes</item>
/// <item><c>b/call1/call2</c> — buddy callsigns</item>
/// <item><c>t/poimqstunw</c> — packet types</item>
/// <item><c>a/latN/lonW/latS/lonE</c> — area (bounding box)</item>
/// <item><c>m/dist</c> — my-range filter (distance from my position)</item>
/// </list>
///
/// Directives not modelled above (e.g. <c>s/</c>, <c>q/</c>, <c>d/</c>) and
/// exclusion directives (<c>-...</c>) are preserved verbatim in
/// <see cref="OtherDirectives"/> so no information is lost on round-trip.
/// </summary>
public sealed class AprsIsFilter
{
    public double? RangeLatitude { get; set; }

    public double? RangeLongitude { get; set; }

    public double? RangeDistanceKm { get; set; }

    public List<string> Prefixes { get; } = [];

    public List<string> Buddies { get; } = [];

    public AprsIsPacketType PacketTypes { get; set; }

    public double? AreaNorthLatitude { get; set; }

    public double? AreaWestLongitude { get; set; }

    public double? AreaSouthLatitude { get; set; }

    public double? AreaEastLongitude { get; set; }

    public double? MyRangeKm { get; set; }

    /// <summary>
    /// Directives that were preserved verbatim (unrecognized or exclusion
    /// directives). Emitted after the modelled directives in output order.
    /// </summary>
    public List<string> OtherDirectives { get; } = [];

    /// <summary>
    /// Serializes the filter to a space-separated APRS-IS filter string in
    /// canonical order: range, prefix, buddy, type, area, my-range, then any
    /// preserved directives.
    /// </summary>
    public string ToFilterString()
    {
        List<string> parts = [];

        if (RangeLatitude.HasValue && RangeLongitude.HasValue && RangeDistanceKm.HasValue)
        {
            parts.Add($"r/{Format(RangeLatitude.Value)}/{Format(RangeLongitude.Value)}/{Format(RangeDistanceKm.Value)}");
        }

        if (Prefixes.Count > 0)
        {
            parts.Add("p/" + string.Join("/", Prefixes));
        }

        if (Buddies.Count > 0)
        {
            parts.Add("b/" + string.Join("/", Buddies));
        }

        string types = ToTypeString(PacketTypes);
        if (types.Length > 0)
        {
            parts.Add("t/" + types);
        }

        if (AreaNorthLatitude.HasValue && AreaWestLongitude.HasValue &&
            AreaSouthLatitude.HasValue && AreaEastLongitude.HasValue)
        {
            parts.Add($"a/{Format(AreaNorthLatitude.Value)}/{Format(AreaWestLongitude.Value)}/" +
                      $"{Format(AreaSouthLatitude.Value)}/{Format(AreaEastLongitude.Value)}");
        }

        if (MyRangeKm.HasValue)
        {
            parts.Add($"m/{Format(MyRangeKm.Value)}");
        }

        parts.AddRange(OtherDirectives);

        return string.Join(" ", parts);
    }

    /// <summary>
    /// Normalizes a stored filter string for use when connecting to an APRS-IS
    /// server. Two directives need runtime context:
    /// <list type="bullet">
    /// <item>A <c>t/</c> directive that selects every packet type is a no-op
    /// that would otherwise subscribe to the full global feed, so it is
    /// dropped.</item>
    /// <item><c>m/dist</c> (my-range) only matches once the server has seen a
    /// position sent from this callsign — which a read-only (unverified)
    /// connection never provides. It is therefore resolved into a fixed
    /// <c>r/lat/lon/dist</c> range around the supplied current position. When
    /// no position is available the <c>m/</c> directive is left as-is.</item>
    /// </list>
    /// </summary>
    public static string NormalizeForConnection(string filter, double? latitude, double? longitude)
    {
        if (string.IsNullOrWhiteSpace(filter))
        {
            return string.Empty;
        }

        AprsIsFilter parsed = Parse(filter);

        if (parsed.PacketTypes == AprsIsPacketType.All)
        {
            parsed.PacketTypes = AprsIsPacketType.None;
        }

        if (parsed.MyRangeKm.HasValue && latitude.HasValue && longitude.HasValue)
        {
            // Prefer an explicitly configured fixed range (r/) over deriving one
            // from the current position. m/ is meaningless on a read-only
            // connection, so when a fixed range is already present it is simply
            // dropped instead of overwriting the user's range.
            if (!parsed.RangeLatitude.HasValue &&
                !parsed.RangeLongitude.HasValue &&
                !parsed.RangeDistanceKm.HasValue)
            {
                parsed.RangeLatitude = latitude.Value;
                parsed.RangeLongitude = longitude.Value;
                parsed.RangeDistanceKm = parsed.MyRangeKm.Value;
            }

            parsed.MyRangeKm = null;
        }

        return parsed.ToFilterString();
    }

    /// <summary>
    /// Parses a space-separated APRS-IS filter string into a structured model.
    /// Unrecognized or exclusion directives are preserved verbatim in
    /// <see cref="OtherDirectives"/>.
    /// </summary>
    public static AprsIsFilter Parse(string filter)
    {
        AprsIsFilter result = new();
        if (string.IsNullOrWhiteSpace(filter))
        {
            return result;
        }

        foreach (string raw in filter.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            string token = raw.Trim();
            if (token.Length == 0)
            {
                continue;
            }

            // Exclusion directives (e.g. "-p/K") are preserved verbatim.
            if (token[0] == '-')
            {
                result.OtherDirectives.Add(token);
                continue;
            }

            int slash = token.IndexOf('/');
            if (slash <= 0)
            {
                result.OtherDirectives.Add(token);
                continue;
            }

            string key = token[..slash].ToLowerInvariant();
            string value = token[(slash + 1)..];
            if (!ParseDirective(result, key, value))
            {
                result.OtherDirectives.Add(token);
            }
        }

        return result;
    }

    private static bool ParseDirective(AprsIsFilter target, string key, string value)
    {
        switch (key)
        {
            case "r":
                if (!TryParseDoubles(value, 3, out double[]? range))
                {
                    return false;
                }
                target.RangeLatitude = range[0];
                target.RangeLongitude = range[1];
                target.RangeDistanceKm = range[2];
                return true;

            case "p":
                List<string> prefixes = SplitParts(value);
                if (prefixes.Count == 0)
                {
                    return false;
                }
                target.Prefixes.AddRange(prefixes);
                return true;

            case "b":
                List<string> buddies = SplitParts(value);
                if (buddies.Count == 0)
                {
                    return false;
                }
                target.Buddies.AddRange(buddies);
                return true;

            case "t":
                AprsIsPacketType flags = AprsIsPacketType.None;
                foreach (char c in value)
                {
                    AprsIsPacketType flag = TypeCharToFlag(c);
                    if (flag == AprsIsPacketType.None)
                    {
                        return false; // unknown type char: preserve whole token
                    }
                    flags |= flag;
                }
                if (flags == AprsIsPacketType.None)
                {
                    return false;
                }
                target.PacketTypes |= flags;
                return true;

            case "a":
                if (!TryParseDoubles(value, 4, out double[]? area))
                {
                    return false;
                }
                target.AreaNorthLatitude = area[0];
                target.AreaWestLongitude = area[1];
                target.AreaSouthLatitude = area[2];
                target.AreaEastLongitude = area[3];
                return true;

            case "m":
                if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double dist))
                {
                    return false;
                }
                target.MyRangeKm = dist;
                return true;

            default:
                return false;
        }
    }

    private static bool TryParseDoubles(string value, int count, out double[] result)
    {
        result = [];
        string[] parts = value.Split('/', StringSplitOptions.None);
        if (parts.Length != count)
        {
            return false;
        }

        double[] parsed = new double[count];
        for (int i = 0; i < count; i++)
        {
            if (!double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out parsed[i]))
            {
                return false;
            }
        }

        result = parsed;
        return true;
    }

    private static List<string> SplitParts(string value) => [.. value.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Trim())
            .Where(part => part.Length > 0)];

    private static AprsIsPacketType TypeCharToFlag(char c) => c switch
    {
        'p' or 'P' => AprsIsPacketType.Position,
        'o' or 'O' => AprsIsPacketType.ObjectPacket,
        'i' or 'I' => AprsIsPacketType.Item,
        'm' or 'M' => AprsIsPacketType.Message,
        'q' or 'Q' => AprsIsPacketType.Query,
        's' or 'S' => AprsIsPacketType.Status,
        't' or 'T' => AprsIsPacketType.Telemetry,
        'u' or 'U' => AprsIsPacketType.UserDefined,
        'n' or 'N' => AprsIsPacketType.Nws,
        'w' or 'W' => AprsIsPacketType.Weather,
        _ => AprsIsPacketType.None,
    };

    private static string ToTypeString(AprsIsPacketType types)
    {
        StringBuilder sb = new();
        AppendType(sb, types, AprsIsPacketType.Position, 'p');
        AppendType(sb, types, AprsIsPacketType.ObjectPacket, 'o');
        AppendType(sb, types, AprsIsPacketType.Item, 'i');
        AppendType(sb, types, AprsIsPacketType.Message, 'm');
        AppendType(sb, types, AprsIsPacketType.Query, 'q');
        AppendType(sb, types, AprsIsPacketType.Status, 's');
        AppendType(sb, types, AprsIsPacketType.Telemetry, 't');
        AppendType(sb, types, AprsIsPacketType.UserDefined, 'u');
        AppendType(sb, types, AprsIsPacketType.Nws, 'n');
        AppendType(sb, types, AprsIsPacketType.Weather, 'w');
        return sb.ToString();
    }

    private static void AppendType(StringBuilder sb, AprsIsPacketType types, AprsIsPacketType flag, char c)
    {
        if ((types & flag) != 0)
        {
            sb.Append(c);
        }
    }

    private static string Format(double value) => value.ToString(CultureInfo.InvariantCulture);
}
