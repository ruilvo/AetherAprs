// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace AetherAprs.Converters;

/// <summary>
/// Converts an enum value to a boolean by comparing it with the converter parameter.
/// Returns true if the value equals the parameter, false otherwise.
/// </summary>
public class EnumToBoolConverter : IValueConverter
{
    public static readonly EnumToBoolConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value == null || parameter == null)
        {
            return false;
        }

        return value.Equals(parameter);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is true && parameter != null)
        {
            return parameter;
        }

        return Avalonia.Data.BindingOperations.DoNothing;
    }
}
