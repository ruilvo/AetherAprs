// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Configuration;
using AetherAprs.Localization;
using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace AetherAprs.Converters;

/// <summary>
/// Converts PacketDisplayTimeRange enum values to localized display strings.
/// </summary>
public class PacketDisplayTimeRangeConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not PacketDisplayTimeRange timeRange)
        {
            return value?.ToString();
        }

        return timeRange switch
        {
            PacketDisplayTimeRange.LastHour => Strings.Get("TimeRangeLastHour"),
            PacketDisplayTimeRange.LastDay => Strings.Get("TimeRangeLastDay"),
            PacketDisplayTimeRange.LastWeek => Strings.Get("TimeRangeLastWeek"),
            PacketDisplayTimeRange.LastMonth => Strings.Get("TimeRangeLastMonth"),
            PacketDisplayTimeRange.All => Strings.Get("TimeRangeAll"),
            PacketDisplayTimeRange.Custom => Strings.Get("TimeRangeCustom"),
            _ => value.ToString()
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        // Not needed for display-only conversion
        throw new NotImplementedException();
    }
}
