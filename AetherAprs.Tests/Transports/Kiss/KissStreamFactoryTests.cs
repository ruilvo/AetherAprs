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
        var factory = new KissStreamFactory(
        [
            new StubConnector(KissTransportKind.Tcp, isSupported: false),
            new StubConnector(KissTransportKind.Tcp, isSupported: true),
            new UnsupportedBluetoothClassicKissStreamConnector()
        ]);

        Assert.Contains(KissTransportKind.Tcp, factory.SupportedTransports);
        Assert.DoesNotContain(KissTransportKind.BluetoothClassic, factory.SupportedTransports);
    }

    [Fact]
    public async Task OpenAsync_UsesSupportedConnectorWhenKindsCollide()
    {
        var supported = new StubConnector(KissTransportKind.Tcp, isSupported: true);
        var unsupported = new StubConnector(KissTransportKind.Tcp, isSupported: false);
        var factory = new KissStreamFactory([unsupported, supported]);

        await using var stream = await factory.OpenAsync(
            new KissSettings
            {
                TransportKind = KissTransportKind.Tcp,
                Transport = new TcpKissTransportSettings()
            },
            TestContext.Current.CancellationToken);

        Assert.Same(supported.LastOpenedStream, stream);
        Assert.Equal(1, supported.ConnectCount);
        Assert.Equal(0, unsupported.ConnectCount);
    }

    [Fact]
    public async Task OpenAsync_MismatchedKindAndTransport_ThrowsInvalidOperationException()
    {
        var factory = new KissStreamFactory([new TcpKissStreamConnector()]);

        await Assert.ThrowsAsync<InvalidOperationException>(() => factory.OpenAsync(
            new KissSettings
            {
                TransportKind = KissTransportKind.Tcp,
                Transport = new BluetoothLeKissTransportSettings()
            },
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task OpenAsync_NullTransport_ThrowsInvalidOperationException()
    {
        var factory = new KissStreamFactory([new TcpKissStreamConnector()]);

        await Assert.ThrowsAsync<InvalidOperationException>(() => factory.OpenAsync(
            new KissSettings
            {
                TransportKind = KissTransportKind.Tcp,
                Transport = null
            },
            TestContext.Current.CancellationToken));
    }

    private sealed class StubConnector : IKissStreamConnector
    {
        public StubConnector(KissTransportKind kind, bool isSupported)
        {
            Kind = kind;
            IsSupported = isSupported;
        }

        public KissTransportKind Kind { get; }

        public bool IsSupported { get; }

        public int ConnectCount { get; private set; }

        public Stream? LastOpenedStream { get; private set; }

        public Task<Stream> ConnectAsync(IKissTransportSettings settings, CancellationToken cancellationToken = default)
        {
            ConnectCount++;
            LastOpenedStream = new MemoryStream();
            return Task.FromResult(LastOpenedStream);
        }
    }
}