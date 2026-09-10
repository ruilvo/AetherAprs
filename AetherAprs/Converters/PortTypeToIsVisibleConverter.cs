// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Globalization;
using Avalonia.Data.Converters;
using AetherAprs.Configuration;

namespace AetherAprs.Converters;

public class PortTypeToIsVisibleConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is PortType portType && parameter is string paramStr)
        {
            if (Enum.TryParse<PortType>(paramStr, out var targetType2))
            {
                return portType == targetType2;
            }
        }

        return false;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
