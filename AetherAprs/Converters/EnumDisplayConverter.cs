// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Globalization;
using Avalonia.Data.Converters;
using AetherAprs.Configuration;
using AetherAprs.Models;

namespace AetherAprs.Converters;

public class EnumDisplayConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null)
        {
            return "Default";
        }

        if (value is Type type)
        {
            if (type == typeof(AprsIsSettings))
            {
                return "APRS-IS";
            }

            if (type == typeof(KissSettings))
            {
                return "KISS";
            }

            if (type == typeof(TcpKissTransportSettings))
            {
                return "TCP";
            }

            if (type == typeof(BluetoothClassicKissTransportSettings))
            {
                return "Bluetooth Classic (SPP)";
            }

            if (type == typeof(BluetoothLeKissTransportSettings))
            {
                return "Bluetooth LE";
            }
        }

        if (value is DynamicBeaconMode mode)
        {
            return mode.ToString();
        }

        return value;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string str)
        {
            return str switch
            {
                "APRS-IS" => typeof(AprsIsSettings),
                "KISS" => typeof(KissSettings),
                "TCP" => typeof(TcpKissTransportSettings),
                "Bluetooth Classic (SPP)" => typeof(BluetoothClassicKissTransportSettings),
                "Bluetooth LE" => typeof(BluetoothLeKissTransportSettings),
                _ => null
            };
        }

        return null;
    }
}