// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Android.Services.Transports;
using Xunit;

namespace AetherAprs.Tests.Android;

/// <summary>
/// Regression tests: Android can report an error status
/// together with <c>Connected</c> (for example GATT_ERROR 133), and that must
/// fail the pending connect instead of leaving it waiting forever.
/// </summary>
public sealed class GattConnectionPolicyTests
{
    [Theory]
    [InlineData(133, true, false)]
    [InlineData(133, false, true)]
    [InlineData(133, false, false)]
    [InlineData(129, true, false)]
    public void Classify_ErrorStatusWithAnyState_FailsPendingConnect(
        int status,
        bool isConnected,
        bool isDisconnected)
    {
        Assert.Equal(
            GattConnectionAction.Fail,
            GattConnectionPolicy.Classify(status, isConnected, isDisconnected));
    }

    [Fact]
    public void Classify_SuccessWithConnected_DiscoversServices()
    {
        Assert.Equal(
            GattConnectionAction.DiscoverServices,
            GattConnectionPolicy.Classify(
                GattConnectionPolicy.SuccessStatus,
                isConnected: true,
                isDisconnected: false));
    }

    [Fact]
    public void Classify_SuccessWithDisconnected_FailsPendingConnect()
    {
        Assert.Equal(
            GattConnectionAction.Fail,
            GattConnectionPolicy.Classify(
                GattConnectionPolicy.SuccessStatus,
                isConnected: false,
                isDisconnected: true));
    }

    [Fact]
    public void Classify_SuccessWithUnknownState_IsIgnored()
    {
        Assert.Equal(
            GattConnectionAction.Ignore,
            GattConnectionPolicy.Classify(
                GattConnectionPolicy.SuccessStatus,
                isConnected: false,
                isDisconnected: false));
    }
}
