// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using Avalonia.Data.Converters;
using System;
using System.Collections;
using System.Globalization;

namespace AetherAprs.Converters.UI;

/// <summary>
/// Converts a collection to a boolean indicating whether it is empty.
/// Returns true when the value is null or has no items, false otherwise.
/// </summary>
public class CollectionEmptyToBoolConverter : IValueConverter
{
    public static readonly CollectionEmptyToBoolConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not IEnumerable collection)
        {
            return true;
        }

        foreach (var _ in collection)
        {
            return false;
        }

        return true;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return Avalonia.Data.BindingOperations.DoNothing;
    }
}
