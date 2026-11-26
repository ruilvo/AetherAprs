// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using AetherAprs.Android.Services.Transports;
using Xunit;

namespace AetherAprs.Tests.Android;

/// <summary>
/// Regression tests: a malformed Bluetooth address must be
/// rejected before it reaches <c>GetRemoteDevice</c>, which throws
/// <c>IllegalArgumentException</c> instead of returning null.
/// </summary>
public sealed class BluetoothDeviceAddressTests
{
    [Theory]
    [InlineData("00:11:22:33:44:55")]
    [InlineData("AA:BB:CC:DD:EE:FF")]
    [InlineData("aa:bb:cc:dd:ee:ff")]
    [InlineData("Aa:bB:Cc:dD:Ee:fF")]
    public void IsValid_AcceptsSixHexOctets(string address)
    {
        Assert.True(BluetoothDeviceAddress.IsValid(address));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("00:11:22:33:44:55:66")]
    [InlineData("00:11:22:33:44")]
    [InlineData("0:11:22:33:44:55")]
    [InlineData("00:11:22:33:44:5")]
    [InlineData("00-11-22-33-44-55")]
    [InlineData("00:11:22:33:44:5G")]
    [InlineData(" 00:11:22:33:44:55")]
    [InlineData("00:11:22:33:44:55 ")]
    [InlineData("001122334455")]
    public void IsValid_RejectsAnythingElse(string? address)
    {
        Assert.False(BluetoothDeviceAddress.IsValid(address));
    }

    [Fact]
    public void Normalize_ReturnsCanonicalUpperCaseAddress()
    {
        Assert.Equal("AA:BB:CC:DD:EE:FF", BluetoothDeviceAddress.Normalize(
            "aa:bb:cc:dd:ee:ff",
            "settings"));
    }

    [Fact]
    public void Normalize_MalformedAddress_ThrowsArgumentException()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            BluetoothDeviceAddress.Normalize("not-an-address", "settings"));

        Assert.Equal("settings", exception.ParamName);
        Assert.Contains(BluetoothDeviceAddress.Format, exception.Message, StringComparison.Ordinal);
    }
}
