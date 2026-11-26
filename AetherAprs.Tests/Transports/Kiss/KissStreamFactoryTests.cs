// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AetherAprs.Configuration;
using AetherAprs.Transports.Kiss;
using Xunit;

namespace AetherAprs.Tests.Transports.Kiss;

public sealed class KissStreamFactoryTests
{
    [Fact]
    public void SupportedTransports_PrefersSupportedConnectorWhenKindsCollide()
    {
        KissStreamFactory factory = new(
        [
            new StubConnector(typeof(TcpKissTransportSettings), isSupported: false),
            new StubConnector(typeof(TcpKissTransportSettings), isSupported: true),
            new UnsupportedBluetoothClassicKissStreamConnector()
        ]);

        Assert.Contains(typeof(TcpKissTransportSettings), factory.SupportedTransports);
        Assert.DoesNotContain(typeof(BluetoothClassicKissTransportSettings), factory.SupportedTransports);
    }

    [Fact]
    public async Task OpenAsync_UsesSupportedConnectorWhenKindsCollide()
    {
        StubConnector supported = new(typeof(TcpKissTransportSettings), isSupported: true);
        StubConnector unsupported = new(typeof(TcpKissTransportSettings), isSupported: false);
        KissStreamFactory factory = new([unsupported, supported]);

        await using Stream stream = await factory.OpenAsync(
            new KissSettings
            {
                Transport = new TcpKissTransportSettings()
            },
            TestContext.Current.CancellationToken);

        Assert.Same(supported.LastOpenedStream, stream);
        Assert.Equal(1, supported.ConnectCount);
        Assert.Equal(0, unsupported.ConnectCount);
    }

    [Fact]
    public async Task OpenAsync_ForwardsCancellationTokenToBothBluetoothConnectors()
    {
        StubConnector classic = new(typeof(BluetoothClassicKissTransportSettings), isSupported: true);
        StubConnector le = new(typeof(BluetoothLeKissTransportSettings), isSupported: true);
        KissStreamFactory factory = new([classic, le]);
        using CancellationTokenSource cts = new();

        await using (Stream stream = await factory.OpenAsync(
            new KissSettings
            {
                Transport = new BluetoothClassicKissTransportSettings()
            },
            cts.Token))
        {
            Assert.Equal(cts.Token, classic.CapturedToken);
        }

        await using (Stream stream = await factory.OpenAsync(
            new KissSettings
            {
                Transport = new BluetoothLeKissTransportSettings()
            },
            cts.Token))
        {
            Assert.Equal(cts.Token, le.CapturedToken);
        }
    }

    [Fact]
    public async Task OpenAsync_UnsupportedTransportType_ThrowsNotSupportedException()
    {
        KissStreamFactory factory = new([new TcpKissStreamConnector()]);

        await Assert.ThrowsAsync<NotSupportedException>(() => factory.OpenAsync(
            new KissSettings
            {
                Transport = new BluetoothLeKissTransportSettings()
            },
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task OpenAsync_NullTransport_ThrowsInvalidOperationException()
    {
        KissStreamFactory factory = new([new TcpKissStreamConnector()]);

        await Assert.ThrowsAsync<InvalidOperationException>(() => factory.OpenAsync(
            new KissSettings
            {
                Transport = null
            },
            TestContext.Current.CancellationToken));
    }

    private sealed class StubConnector(Type settingsType, bool isSupported) : IKissStreamConnector
    {
        public Type SettingsType { get; } = settingsType;

        public bool IsSupported { get; } = isSupported;

        public int ConnectCount { get; private set; }

        public CancellationToken CapturedToken { get; private set; }

        public Stream? LastOpenedStream { get; private set; }

        public Task<Stream> ConnectAsync(IKissTransportSettings settings, CancellationToken cancellationToken = default)
        {
            ConnectCount++;
            CapturedToken = cancellationToken;
            LastOpenedStream = new MemoryStream();
            return Task.FromResult(LastOpenedStream);
        }
    }
}
