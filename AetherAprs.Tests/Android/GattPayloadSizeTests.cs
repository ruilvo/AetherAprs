// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Android.Services.Transports;
using Xunit;

namespace AetherAprs.Tests.Android;

/// <summary>
/// Regression tests: Android's stack truncates a write larger than the negotiated
/// ATT payload and still reports success, so the usable size has to be derived
/// exactly and must never collapse to zero.
/// </summary>
public sealed class GattPayloadSizeTests
{
    [Fact]
    public void FromMtu_DefaultMtu_ReturnsTwentyBytes()
    {
        Assert.Equal(20, GattPayloadSize.FromMtu(23));
    }

    [Fact]
    public void FromMtu_RequestedMtu_ReturnsMaxPayload()
    {
        Assert.Equal(
            GattPayloadSize.MaxPayloadSize,
            GattPayloadSize.FromMtu(GattPayloadSize.RequestedAttMtu));
    }

    [Fact]
    public void FromMtu_MtuAboveTheRequestedCeiling_IsClamped()
    {
        Assert.Equal(GattPayloadSize.MaxPayloadSize, GattPayloadSize.FromMtu(1500));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(3)]
    public void FromMtu_UnusableMtu_FallsBackToTheBaselinePayload(int mtu)
    {
        // An MTU that cannot carry a single payload byte would make the frame writer
        // throw, so it falls back to the size every Bluetooth LE peer supports.
        Assert.Equal(GattPayloadSize.DefaultPayloadSize, GattPayloadSize.FromMtu(mtu));
    }

    [Fact]
    public void FromMtu_JustAboveTheHeader_ReturnsOneByte()
    {
        Assert.Equal(1, GattPayloadSize.FromMtu(GattPayloadSize.AttHeaderSize + 1));
    }
}
