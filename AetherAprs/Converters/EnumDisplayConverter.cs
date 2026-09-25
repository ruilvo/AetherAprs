// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Configuration;
using AetherAprs.Localization;
using AetherAprs.Models;
using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace AetherAprs.Converters;

public class EnumDisplayConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null)
        {
            return Strings.Get("Default");
        }

        if (value is Type type)
        {
            if (type == typeof(AprsIsSettings))
            {
                return Strings.Get("PortTypeAprsIs");
            }

            if (type == typeof(KissSettings))
            {
                return Strings.Get("PortTypeKiss");
            }

            if (type == typeof(TcpKissTransportSettings))
            {
                return Strings.Get("TransportTcp");
            }

            if (type == typeof(BluetoothClassicKissTransportSettings))
            {
                return Strings.Get("TransportBluetoothClassicSpp");
            }

            if (type == typeof(BluetoothLeKissTransportSettings))
            {
                return Strings.Get("TransportBluetoothLe");
            }
        }

        if (value is DynamicBeaconMode mode)
        {
            return mode switch
            {
                DynamicBeaconMode.Walk => Strings.Get("BeaconModeWalk"),
                DynamicBeaconMode.Drive => Strings.Get("BeaconModeDrive"),
                DynamicBeaconMode.Custom => Strings.Get("BeaconModeCustom"),
                _ => mode.ToString()
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
                _ when str == Strings.Get("PortTypeAprsIs") || str == "APRS-IS" => typeof(AprsIsSettings),
                _ when str == Strings.Get("PortTypeKiss") || str == "KISS" => typeof(KissSettings),
                _ when str == Strings.Get("TransportTcp") || str == "TCP" => typeof(TcpKissTransportSettings),
                _ when str == Strings.Get("TransportBluetoothClassicSpp") || str == "Bluetooth Classic (SPP)" => typeof(BluetoothClassicKissTransportSettings),
                _ when str == Strings.Get("TransportBluetoothLe") || str == "Bluetooth LE" => typeof(BluetoothLeKissTransportSettings),
                _ => null
            };
        }

        return null;
    }
}
