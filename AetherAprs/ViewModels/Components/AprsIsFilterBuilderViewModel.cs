// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Models.Aprs;
using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace AetherAprs.ViewModels.Components;

/// <summary>
/// ViewModel for the APRS-IS filter builder component. Exposes editable fields
/// for each supported filter directive and computes the resulting filter string
/// via <see cref="Filter"/>. Existing filter strings can be round-tripped into
/// the UI with <see cref="Load"/>.
/// </summary>
public partial class AprsIsFilterBuilderViewModel : ViewModelBase
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Filter))]
    public partial string RangeLatitude { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Filter))]
    public partial string RangeLongitude { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Filter))]
    public partial string RangeDistanceKm { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Filter))]
    public partial string Prefixes { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Filter))]
    public partial string Buddies { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Filter))]
    public partial bool IncludePosition { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Filter))]
    public partial bool IncludeObject { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Filter))]
    public partial bool IncludeItem { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Filter))]
    public partial bool IncludeMessage { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Filter))]
    public partial bool IncludeQuery { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Filter))]
    public partial bool IncludeStatus { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Filter))]
    public partial bool IncludeTelemetry { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Filter))]
    public partial bool IncludeUserDefined { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Filter))]
    public partial bool IncludeNws { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Filter))]
    public partial bool IncludeWeather { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Filter))]
    public partial string AreaNorth { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Filter))]
    public partial string AreaWest { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Filter))]
    public partial string AreaSouth { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Filter))]
    public partial string AreaEast { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Filter))]
    public partial string MyRangeKm { get; set; } = string.Empty;

    /// <summary>
    /// Preserved directives that cannot be represented by the builder fields
    /// (unrecognized or exclusion directives). Kept so that loading an existing
    /// filter and re-saving it does not lose information.
    /// </summary>
    private readonly List<string> _otherDirectives = [];

    /// <summary>
    /// Gets the current filter string built from the editable fields, in
    /// canonical order. Unknown/preserved directives are appended last.
    /// </summary>
    public string Filter
    {
        get
        {
            var filter = new AprsIsFilter();

            if (TryParse(RangeLatitude, out var lat) &&
                TryParse(RangeLongitude, out var lon) &&
                TryParse(RangeDistanceKm, out var dist))
            {
                filter.RangeLatitude = lat;
                filter.RangeLongitude = lon;
                filter.RangeDistanceKm = dist;
            }

            AddList(filter.Prefixes, Prefixes);
            AddList(filter.Buddies, Buddies);
            filter.PacketTypes = BuildPacketTypes();

            if (TryParse(AreaNorth, out var north) &&
                TryParse(AreaWest, out var west) &&
                TryParse(AreaSouth, out var south) &&
                TryParse(AreaEast, out var east))
            {
                filter.AreaNorthLatitude = north;
                filter.AreaWestLongitude = west;
                filter.AreaSouthLatitude = south;
                filter.AreaEastLongitude = east;
            }

            if (TryParse(MyRangeKm, out var myRange))
            {
                filter.MyRangeKm = myRange;
            }

            filter.OtherDirectives.AddRange(_otherDirectives);

            return filter.ToFilterString();
        }
    }

    /// <summary>
    /// Populates the editable fields from an existing filter string, preserving
    /// any directives that cannot be represented by the builder.
    /// </summary>
    public void Load(string? filter)
    {
        Reset();

        if (string.IsNullOrWhiteSpace(filter))
        {
            return;
        }

        var parsed = AprsIsFilter.Parse(filter);

        RangeLatitude = Format(parsed.RangeLatitude);
        RangeLongitude = Format(parsed.RangeLongitude);
        RangeDistanceKm = Format(parsed.RangeDistanceKm);
        Prefixes = string.Join(" ", parsed.Prefixes);
        Buddies = string.Join(" ", parsed.Buddies);
        SetPacketTypes(parsed.PacketTypes);
        AreaNorth = Format(parsed.AreaNorthLatitude);
        AreaWest = Format(parsed.AreaWestLongitude);
        AreaSouth = Format(parsed.AreaSouthLatitude);
        AreaEast = Format(parsed.AreaEastLongitude);
        MyRangeKm = Format(parsed.MyRangeKm);

        _otherDirectives.Clear();
        _otherDirectives.AddRange(parsed.OtherDirectives);
    }

    private void Reset()
    {
        RangeLatitude = string.Empty;
        RangeLongitude = string.Empty;
        RangeDistanceKm = string.Empty;
        Prefixes = string.Empty;
        Buddies = string.Empty;
        IncludePosition = false;
        IncludeObject = false;
        IncludeItem = false;
        IncludeMessage = false;
        IncludeQuery = false;
        IncludeStatus = false;
        IncludeTelemetry = false;
        IncludeUserDefined = false;
        IncludeNws = false;
        IncludeWeather = false;
        AreaNorth = string.Empty;
        AreaWest = string.Empty;
        AreaSouth = string.Empty;
        AreaEast = string.Empty;
        MyRangeKm = string.Empty;
        _otherDirectives.Clear();
    }

    private AprsIsPacketType BuildPacketTypes()
    {
        var types = AprsIsPacketType.None;
        if (IncludePosition) types |= AprsIsPacketType.Position;
        if (IncludeObject) types |= AprsIsPacketType.ObjectPacket;
        if (IncludeItem) types |= AprsIsPacketType.Item;
        if (IncludeMessage) types |= AprsIsPacketType.Message;
        if (IncludeQuery) types |= AprsIsPacketType.Query;
        if (IncludeStatus) types |= AprsIsPacketType.Status;
        if (IncludeTelemetry) types |= AprsIsPacketType.Telemetry;
        if (IncludeUserDefined) types |= AprsIsPacketType.UserDefined;
        if (IncludeNws) types |= AprsIsPacketType.Nws;
        if (IncludeWeather) types |= AprsIsPacketType.Weather;
        return types;
    }

    private void SetPacketTypes(AprsIsPacketType types)
    {
        IncludePosition = (types & AprsIsPacketType.Position) != 0;
        IncludeObject = (types & AprsIsPacketType.ObjectPacket) != 0;
        IncludeItem = (types & AprsIsPacketType.Item) != 0;
        IncludeMessage = (types & AprsIsPacketType.Message) != 0;
        IncludeQuery = (types & AprsIsPacketType.Query) != 0;
        IncludeStatus = (types & AprsIsPacketType.Status) != 0;
        IncludeTelemetry = (types & AprsIsPacketType.Telemetry) != 0;
        IncludeUserDefined = (types & AprsIsPacketType.UserDefined) != 0;
        IncludeNws = (types & AprsIsPacketType.Nws) != 0;
        IncludeWeather = (types & AprsIsPacketType.Weather) != 0;
    }

    private static void AddList(List<string> target, string source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return;
        }

        foreach (var item in source.Split([' ', ',', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = item.Trim();
            if (trimmed.Length > 0)
            {
                target.Add(trimmed);
            }
        }
    }

    private static bool TryParse(string? text, out double value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    private static string Format(double? value) =>
        value?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
}
