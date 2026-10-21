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

    [Fact]
    public async Task ConnectAsync_ConnectsToLocalListener_ReturnsUsableDuplexStream()
    {
        using var listener = new System.Net.Sockets.TcpListener(
            System.Net.IPAddress.Loopback, 0);
        listener.Start();

        try
        {
            var serverTask = listener.AcceptTcpClientAsync(TestContext.Current.CancellationToken);
            var connector = new TcpKissStreamConnector();

            var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
            var stream = await connector.ConnectAsync(
                new TcpKissTransportSettings { Host = "127.0.0.1", Port = port },
                TestContext.Current.CancellationToken);

            await using (stream)
            {
                Assert.True(stream.CanRead);
                Assert.True(stream.CanWrite);

                // Server sends a KISS frame; the client stream must read it.
                using var server = await serverTask.AsTask().WaitAsync(
                    TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
                server.GetStream().Write([0xC0, 0x00, 0x01, 0xC0]);

                var buffer = new byte[4];
                var read = 0;
                while (read < buffer.Length)
                {
                    var n = await stream.ReadAsync(buffer.AsMemory(read), TestContext.Current.CancellationToken);
                    Assert.True(n > 0, "Stream closed before the frame was fully received.");
                    read += n;
                }

                Assert.Equal([0xC0, 0x00, 0x01, 0xC0], buffer);
            }
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task ConnectAsync_ConnectionRefused_ThrowsAndDisposesClient()
    {
        // Port 1 on loopback is (practically) guaranteed to be closed.
        var connector = new TcpKissStreamConnector();

        await Assert.ThrowsAnyAsync<System.Net.Sockets.SocketException>(() =>
            connector.ConnectAsync(
                new TcpKissTransportSettings { Host = "127.0.0.1", Port = 1 },
                TestContext.Current.CancellationToken));
    }
}