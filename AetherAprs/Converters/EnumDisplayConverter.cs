// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Globalization;
using Avalonia.Data.Converters;
using AetherAprs.Configuration;

namespace AetherAprs.Converters;

public class EnumDisplayConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is PortType portType)
        {
            return portType switch
            {
                PortType.AprsIs => "APRS-IS",
                PortType.Kiss => "KISS",
                _ => value.ToString()
            };
        }

        return value;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string str)
        {
            return str switch
            {
                "APRS-IS" => PortType.AprsIs,
                "KISS" => PortType.Kiss,
                _ => null
            };
        }

        return null;
    }
}
