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
            DateTime dt => dt.Kind == DateTimeKind.Utc
                ? dt.ToLocalTime()
                : dt, // If not UTC, return as-is
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
