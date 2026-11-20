// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Configuration;
using AetherAprs.Localization;
using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace AetherAprs.Converters.UI;

/// <summary>
/// Converts <see cref="TileSourceType"/> enum values to localized display strings.
/// </summary>
public class TileSourceTypeConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not TileSourceType tileSource)
        {
            return value?.ToString();
        }

        return tileSource switch
        {
            TileSourceType.OsmOnline => Strings.Get("TileSourceOsmOnline"),
            TileSourceType.MbTilesOffline => Strings.Get("TileSourceMbTilesOffline"),
            _ => value.ToString()
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        // Not needed for display-only conversion
        throw new NotImplementedException();
    }
}
