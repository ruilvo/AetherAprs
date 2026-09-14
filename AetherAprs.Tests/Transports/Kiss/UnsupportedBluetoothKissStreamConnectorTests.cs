// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Threading.Tasks;
using AetherAprs.Configuration;
using AetherAprs.Transports.Kiss;
using Xunit;

namespace AetherAprs.Tests.Transports.Kiss;

public sealed class UnsupportedBluetoothKissStreamConnectorTests
{
    [Fact]
    public void ClassicConnector_ReportsUnsupportedAndSettingsType()
    {
        var connector = new UnsupportedBluetoothClassicKissStreamConnector();

        Assert.False(connector.IsSupported);
        Assert.Equal(typeof(BluetoothClassicKissTransportSettings), connector.SettingsType);
    }

    [Fact]
    public async Task ClassicConnector_ConnectAsync_ThrowsPlatformNotSupported()
    {
        var connector = new UnsupportedBluetoothClassicKissStreamConnector();

        var ex = await Assert.ThrowsAsync<PlatformNotSupportedException>(() =>
            connector.ConnectAsync(new BluetoothClassicKissTransportSettings(), TestContext.Current.CancellationToken));

        Assert.Contains("Bluetooth Classic KISS", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LeConnector_ReportsUnsupportedAndSettingsType()
    {
        var connector = new UnsupportedBluetoothLeKissStreamConnector();

        Assert.False(connector.IsSupported);
        Assert.Equal(typeof(BluetoothLeKissTransportSettings), connector.SettingsType);
    }

    [Fact]
    public async Task LeConnector_ConnectAsync_ThrowsPlatformNotSupported()
    {
        var connector = new UnsupportedBluetoothLeKissStreamConnector();

        var ex = await Assert.ThrowsAsync<PlatformNotSupportedException>(() =>
            connector.ConnectAsync(new BluetoothLeKissTransportSettings(), TestContext.Current.CancellationToken));

        Assert.Contains("Bluetooth LE KISS", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
