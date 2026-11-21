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
/// ViewModel for the supported APRS-IS automatic filter fields: distance,
/// prefixes, and packet types.
/// </summary>
public partial class AprsIsFilterBuilderViewModel : ViewModelBase
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Filter))]
    public partial string Prefixes { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Filter))]
    public partial bool IncludePosition { get; set; } = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Filter))]
    public partial bool IncludeObject { get; set; } = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Filter))]
    public partial bool IncludeItem { get; set; } = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Filter))]
    public partial bool IncludeMessage { get; set; } = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Filter))]
    public partial bool IncludeQuery { get; set; } = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Filter))]
    public partial bool IncludeStatus { get; set; } = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Filter))]
    public partial bool IncludeTelemetry { get; set; } = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Filter))]
    public partial bool IncludeUserDefined { get; set; } = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Filter))]
    public partial bool IncludeNws { get; set; } = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Filter))]
    public partial bool IncludeWeather { get; set; } = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Filter))]
    public partial string MyRangeKm { get; set; } = "100";

    /// <summary>Gets the current supported automatic filter string.</summary>
    public string Filter
    {
        get
        {
            var filter = new AprsIsFilter();

            AddList(filter.Prefixes, Prefixes);

            // A type filter that selects every type (or none) is a no-op that
            // would otherwise subscribe to the full global feed, so only emit
            // t/ when the user has chosen a meaningful subset.
            var types = BuildPacketTypes();
            if (types != AprsIsPacketType.All)
            {
                filter.PacketTypes = types;
            }

            if (TryParse(MyRangeKm, out var myRange))
            {
                filter.MyRangeKm = myRange;
            }

            return filter.ToFilterString();
        }
    }

    /// <summary>
    /// Populates the supported fields from an existing filter string. Other
    /// directives are intentionally ignored.
    /// </summary>
    public void Load(string? filter)
    {
        Reset();

        if (string.IsNullOrWhiteSpace(filter))
        {
            return;
        }

        var parsed = AprsIsFilter.Parse(filter);

        Prefixes = string.Join(" ", parsed.Prefixes);
        SetPacketTypes(parsed.PacketTypes);
        MyRangeKm = Format(parsed.MyRangeKm);
    }

    private void Reset()
    {
        Prefixes = string.Empty;
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
        MyRangeKm = string.Empty;
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
