// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Localization;
using AetherAprs.Models.Aprs;
using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace AetherAprs.Converters.Aprs;

/// <summary>
/// Converts <see cref="AprsSsid"/> enum values to localized display strings.
/// </summary>
public class AprsSsidConverter : IValueConverter
{
    public static readonly AprsSsidConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not AprsSsid ssid)
        {
            return null;
        }

        return ssid switch
        {
            AprsSsid.PrimaryStation => Strings.Get("SsidPrimaryStation"),
            AprsSsid.AdditionalStation1 => Strings.Get("SsidAdditionalStation1"),
            AprsSsid.AdditionalStation2 => Strings.Get("SsidAdditionalStation2"),
            AprsSsid.AdditionalStation3 => Strings.Get("SsidAdditionalStation3"),
            AprsSsid.AdditionalStation4 => Strings.Get("SsidAdditionalStation4"),
            AprsSsid.OtherNetworks => Strings.Get("SsidOtherNetworks"),
            AprsSsid.SpecialActivity => Strings.Get("SsidSpecialActivity"),
            AprsSsid.HumanPortable => Strings.Get("SsidHumanPortable"),
            AprsSsid.SecondaryMobile => Strings.Get("SsidSecondaryMobile"),
            AprsSsid.PrimaryMobile => Strings.Get("SsidPrimaryMobile"),
            AprsSsid.InternetGateway => Strings.Get("SsidInternetGateway"),
            AprsSsid.Aircraft => Strings.Get("SsidAircraft"),
            AprsSsid.Devices => Strings.Get("SsidDevices"),
            AprsSsid.WeatherStation => Strings.Get("SsidWeatherStation"),
            AprsSsid.Trucker => Strings.Get("SsidTrucker"),
            AprsSsid.AdditionalStation15 => Strings.Get("SsidAdditionalStation15"),
            _ => ssid.ToString()
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string str)
        {
            return null;
        }

        // Try to match against localized strings
        if (str == Strings.Get("SsidPrimaryStation")) return AprsSsid.PrimaryStation;
        if (str == Strings.Get("SsidAdditionalStation1")) return AprsSsid.AdditionalStation1;
        if (str == Strings.Get("SsidAdditionalStation2")) return AprsSsid.AdditionalStation2;
        if (str == Strings.Get("SsidAdditionalStation3")) return AprsSsid.AdditionalStation3;
        if (str == Strings.Get("SsidAdditionalStation4")) return AprsSsid.AdditionalStation4;
        if (str == Strings.Get("SsidOtherNetworks")) return AprsSsid.OtherNetworks;
        if (str == Strings.Get("SsidSpecialActivity")) return AprsSsid.SpecialActivity;
        if (str == Strings.Get("SsidHumanPortable")) return AprsSsid.HumanPortable;
        if (str == Strings.Get("SsidSecondaryMobile")) return AprsSsid.SecondaryMobile;
        if (str == Strings.Get("SsidPrimaryMobile")) return AprsSsid.PrimaryMobile;
        if (str == Strings.Get("SsidInternetGateway")) return AprsSsid.InternetGateway;
        if (str == Strings.Get("SsidAircraft")) return AprsSsid.Aircraft;
        if (str == Strings.Get("SsidDevices")) return AprsSsid.Devices;
        if (str == Strings.Get("SsidWeatherStation")) return AprsSsid.WeatherStation;
        if (str == Strings.Get("SsidTrucker")) return AprsSsid.Trucker;
        if (str == Strings.Get("SsidAdditionalStation15")) return AprsSsid.AdditionalStation15;

        return null;
    }
}
