// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Globalization;

using AetherAprs.Configuration;
using AetherAprs.Localization;

using Avalonia.Data.Converters;

namespace AetherAprs.Converters.UI;

/// <summary>
/// Converts <see cref="ThemeMode"/> enum values to localized display strings.
/// </summary>
public class ThemeModeConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not ThemeMode mode)
        {
            return value?.ToString();
        }

        return mode switch
        {
            ThemeMode.System => Strings.Get("ThemeModeSystem", culture),
            ThemeMode.Light => Strings.Get("ThemeModeLight", culture),
            ThemeMode.Dark => Strings.Get("ThemeModeDark", culture),
            _ => value.ToString()
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        // This converter is display-only; do not write the localized text back to the source.
        Avalonia.Data.BindingOperations.DoNothing;
}
