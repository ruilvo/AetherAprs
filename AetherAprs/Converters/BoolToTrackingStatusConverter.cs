// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace AetherAprs.Converters;

/// <summary>
/// Converts a tracking boolean into a short status label.
/// </summary>
public sealed class BoolToTrackingStatusConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is true ? "Tracking" : "Stopped";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
