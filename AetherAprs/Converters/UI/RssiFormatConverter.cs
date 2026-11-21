// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Localization;
using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace AetherAprs.Converters.UI;

/// <summary>
/// Converts RSSI integer values to localized formatted strings (e.g., "RSSI: -65 dBm").
/// </summary>
public class RssiFormatConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not int rssi)
        {
            return value?.ToString();
        }

        return string.Format(culture, Strings.Get("RssiFormat", culture), rssi);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        // This converter is display-only; do not write the localized text back to the source.
        return Avalonia.Data.BindingOperations.DoNothing;
    }
}
