// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Threading.Tasks;
using AetherAprs.Configuration;
using AetherAprs.Transports.Kiss;
using Xunit;

namespace AetherAprs.Tests.Transports.Kiss;

public sealed class TcpKissStreamConnectorTests
{
    [Fact]
    public async Task ConnectAsync_WrongSettingsType_ThrowsArgumentException()
    {
        var connector = new TcpKissStreamConnector();

        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => connector.ConnectAsync(new BluetoothClassicKissTransportSettings(), TestContext.Current.CancellationToken));

        Assert.Equal("settings", exception.ParamName);
    }

    [Fact]
    public void SettingsType_IsTcpAndSupported()
    {
        var connector = new TcpKissStreamConnector();

        Assert.Equal(typeof(TcpKissTransportSettings), connector.SettingsType);
        Assert.True(connector.IsSupported);
    }
}