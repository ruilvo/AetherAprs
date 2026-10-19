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
/// Use <see cref="InstanceInverted"/> for the opposite polarity.
/// </summary>
public class CollectionEmptyToBoolConverter : IValueConverter
{
    public static readonly CollectionEmptyToBoolConverter Instance = new(invert: false);

    public static readonly CollectionEmptyToBoolConverter InstanceInverted = new(invert: true);

    private readonly bool _invert;

    private CollectionEmptyToBoolConverter(bool invert)
    {
        _invert = invert;
    }

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var isEmpty = value is not IEnumerable collection || !collection.GetEnumerator().MoveNext();

        return _invert ? !isEmpty : isEmpty;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return Avalonia.Data.BindingOperations.DoNothing;
    }
}
