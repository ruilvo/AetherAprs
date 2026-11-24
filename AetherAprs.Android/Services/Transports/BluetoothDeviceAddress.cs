// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;

namespace AetherAprs.Android.Services.Transports;

/// <summary>
/// Validates and normalizes Bluetooth device addresses before they reach the
/// Android stack.
/// </summary>
/// <remarks>
/// <c>BluetoothAdapter.GetRemoteDevice(string)</c> throws
/// <c>Java.Lang.IllegalArgumentException</c> for a malformed address instead of
/// returning <see langword="null"/>, so the format has to be checked first
/// (audit P-19). Android accepts exactly six colon-separated hexadecimal octets
/// in either case; <see cref="Normalize"/> also upper-cases the result because
/// that is the canonical form the platform reports.
/// </remarks>
internal static class BluetoothDeviceAddress
{
    /// <summary>Human-readable form accepted by Android.</summary>
    internal const string Format = "XX:XX:XX:XX:XX:XX";

    /// <summary>Length of a canonical Bluetooth device address.</summary>
    private const int AddressLength = 17;

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="address"/> is exactly
    /// six colon-separated hexadecimal octets (<c>XX:XX:XX:XX:XX:XX</c>).
    /// </summary>
    public static bool IsValid(string? address)
    {
        if (address is null || address.Length != AddressLength)
        {
            return false;
        }

        for (var index = 0; index < address.Length; index++)
        {
            var character = address[index];
            var isSeparator = index % 3 == 2;
            if (isSeparator)
            {
                if (character != ':')
                {
                    return false;
                }
            }
            else if (!IsHexDigit(character))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Validates <paramref name="address"/> and returns the canonical
    /// upper-case form.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// <paramref name="address"/> is not in <see cref="Format"/> form.
    /// </exception>
    public static string Normalize(string address, string parameterName)
    {
        if (!IsValid(address))
        {
            throw new ArgumentException(
                $"Bluetooth device address must use the {Format} format with hexadecimal digits.",
                parameterName);
        }

        return address.ToUpperInvariant();
    }

    private static bool IsHexDigit(char character) =>
        character is >= '0' and <= '9'
            or >= 'A' and <= 'F'
            or >= 'a' and <= 'f';
}
