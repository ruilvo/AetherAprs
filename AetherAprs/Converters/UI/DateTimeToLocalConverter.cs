// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace AetherAprs.Converters.UI;

/// <summary>
/// Converts UTC DateTime or DateTimeOffset to local time for display.
/// </summary>
public class DateTimeToLocalConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value switch
        {
            // SQLite round-trips DateTime as DateTimeKind.Unspecified, so packet
            // timestamps from the data layer arrive here without a Kind. The data
            // layer stores UTC exclusively (AGENTS.md), so an Unspecified value is
            // UTC and must be localised rather than shown verbatim.
            DateTime dt => dt.Kind switch
            {
                DateTimeKind.Utc => dt.ToLocalTime(),
                DateTimeKind.Unspecified => DateTime.SpecifyKind(dt, DateTimeKind.Utc).ToLocalTime(),
                _ => dt
            },
            DateTimeOffset dto => dto.ToLocalTime(),
            _ => value
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        // Not needed for display-only binding
        throw new NotSupportedException("DateTimeToLocalConverter only supports one-way binding.");
    }
}
