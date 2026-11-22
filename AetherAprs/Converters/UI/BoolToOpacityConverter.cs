// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace AetherAprs.Converters.UI;

/// <summary>
/// Converts a boolean to an opacity value.
/// Returns 1.0 (fully visible) when true, 0.4 (faded) when false.
/// </summary>
public class BoolToOpacityConverter : IValueConverter
{
    public static readonly BoolToOpacityConverter Instance = new();

    private const double VisibleOpacity = 1.0;
    private const double FadedOpacity = 0.4;

    private BoolToOpacityConverter()
    {
    }

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            return boolValue ? VisibleOpacity : FadedOpacity;
        }

        return FadedOpacity;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return Avalonia.Data.BindingOperations.DoNothing;
    }
}
