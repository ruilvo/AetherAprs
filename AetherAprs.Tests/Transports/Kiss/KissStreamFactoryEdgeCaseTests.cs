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

public sealed class KissStreamFactoryEdgeCaseTests
{
    [Fact]
    public void Constructor_NullConnectors_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new KissStreamFactory(null!));
    }

    [Fact]
    public void SupportedTransports_NoSupportedConnectors_ReturnsEmpty()
    {
        var factory = new KissStreamFactory(
        [
            new UnsupportedBluetoothLeKissStreamConnector(),
            new UnsupportedBluetoothClassicKissStreamConnector()
        ]);

        Assert.Empty(factory.SupportedTransports);
    }

    [Fact]
    public async Task OpenAsync_OnlyUnsupportedConnectorRegistered_ThrowsPlatformNotSupported()
    {
        // Documents current behaviour: an unsupported connector is still
        // invoked and fails with PlatformNotSupportedException.
        var factory = new KissStreamFactory([new UnsupportedBluetoothLeKissStreamConnector()]);

        await Assert.ThrowsAsync<PlatformNotSupportedException>(() => factory.OpenAsync(
            new KissSettings
            {
                Transport = new BluetoothLeKissTransportSettings()
            },
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task OpenAsync_PassesCancellationTokenToConnector()
    {
        var connector = new CancellingStubConnector();
        var factory = new KissStreamFactory([connector]);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => factory.OpenAsync(
            new KissSettings
            {
                Transport = new TcpKissTransportSettings()
            },
            cts.Token));

        Assert.True(connector.TokenWasCancelled);
    }

    private sealed class CancellingStubConnector : IKissStreamConnector
    {
        public Type SettingsType => typeof(TcpKissTransportSettings);

        public bool IsSupported => true;

        public bool TokenWasCancelled { get; private set; }

        public Task<Stream> ConnectAsync(IKissTransportSettings settings, CancellationToken cancellationToken = default)
        {
            TokenWasCancelled = cancellationToken.IsCancellationRequested;
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<Stream>(new MemoryStream());
        }
    }
}
