// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Localization;
using AetherAprs.Models.Aprs;
using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace AetherAprs.Converters.UI;

/// <summary>
/// Converts <see cref="PositionFormat"/> enum values to localized display strings.
/// </summary>
public class PositionFormatConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not PositionFormat format)
        {
            return value?.ToString();
        }

        return format switch
        {
            PositionFormat.Uncompressed => Strings.Get("PositionFormatUncompressed", culture),
            PositionFormat.Compressed => Strings.Get("PositionFormatCompressed", culture),
            PositionFormat.MicE => Strings.Get("PositionFormatMicE", culture),
            _ => value.ToString()
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        // This converter is display-only; do not write the localized text back to the source.
        Avalonia.Data.BindingOperations.DoNothing;
}
