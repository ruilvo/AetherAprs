// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace AetherAprs.Converters.UI;

public class TypeNameToIsVisibleConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is Type t && parameter is string name && t.Name == name;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        // Not needed for display-only conversion
        throw new NotImplementedException();
    }
}
