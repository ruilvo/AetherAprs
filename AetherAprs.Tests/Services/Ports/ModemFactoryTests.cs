// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using AetherAprs.Configuration;
using AetherAprs.Services.Ports;
using AetherAprs.Tests.Helpers;
using AetherAprs.Transports.Kiss;

using Xunit;

namespace AetherAprs.Tests.Services.Ports;

/// <summary>
/// One case per configured transport, plus the two ways a port can name a
/// transport that cannot be built.
/// </summary>
public sealed class ModemFactoryTests
{
    [Fact]
    public async Task CreateAsync_AprsIsPort_BuildsAprsIsModem()
    {
        ModemFactory factory = TestModemFactory.Create();

        await using IPortModem modem = await factory.CreateAsync(
            CreatePort(new AprsIsSettings()),
            TestContext.Current.CancellationToken);

        Assert.Equal("APRS-IS", modem.TransportName);
    }

    [Fact]
    public async Task CreateAsync_KissPort_BuildsKissModem()
    {
        ModemFactory factory = TestModemFactory.Create(
            kissStreamFactory: new StubKissStreamFactory(new MemoryStream()));

        await using IPortModem modem = await factory.CreateAsync(
            CreatePort(new KissSettings
            {
                Transport = new TcpKissTransportSettings { Host = "127.0.0.1", Port = 8001 }
            }),
            TestContext.Current.CancellationToken);

        Assert.Equal("KISS", modem.TransportName);
    }

    [Fact]
    public async Task CreateAsync_AfskPort_BuildsAfskModem()
    {
        ModemFactory factory = TestModemFactory.Create();

        await using IPortModem modem = await factory.CreateAsync(
            CreatePort(new AfskSettings()),
            TestContext.Current.CancellationToken);

        Assert.Equal("AFSK", modem.TransportName);
    }

    [Fact]
    public async Task CreateAsync_MissingTypeSettings_IsRejected()
    {
        ModemFactory factory = TestModemFactory.Create();

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => factory.CreateAsync(
                CreatePort(null),
                TestContext.Current.CancellationToken));

        Assert.Contains("Port", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateAsync_UnknownSettingsType_IsRejected()
    {
        ModemFactory factory = TestModemFactory.Create();

        NotSupportedException exception = await Assert.ThrowsAsync<NotSupportedException>(
            () => factory.CreateAsync(
                CreatePort(new UnregisteredPortTypeSettings()),
                TestContext.Current.CancellationToken));

        Assert.Contains(
            nameof(UnregisteredPortTypeSettings),
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateAsync_KissPortWithoutTransport_IsRejected()
    {
        ModemFactory factory = TestModemFactory.Create(
            kissStreamFactory: new KissStreamFactory([new TcpKissStreamConnector()]));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => factory.CreateAsync(
                CreatePort(new KissSettings { Transport = null }),
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CreateAsync_KissPortWithUnregisteredTransport_IsRejected()
    {
        ModemFactory factory = TestModemFactory.Create(
            kissStreamFactory: new KissStreamFactory([new TcpKissStreamConnector()]));

        await Assert.ThrowsAsync<NotSupportedException>(
            () => factory.CreateAsync(
                CreatePort(new KissSettings
                {
                    Transport = new BluetoothLeKissTransportSettings { DeviceAddress = "AA:BB:CC:DD:EE:FF" }
                }),
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CreateAsync_ForwardsCancellationToTheConnector()
    {
        RecordingConnector connector = new();
        ModemFactory factory = TestModemFactory.Create(
            kissStreamFactory: new KissStreamFactory([connector]));

        using CancellationTokenSource cts = new();
        IPortModem modem = await factory.CreateAsync(
            CreatePort(new KissSettings
            {
                Transport = new TcpKissTransportSettings { Host = "127.0.0.1", Port = 8001 }
            }),
            cts.Token);

        Assert.Equal(cts.Token, connector.CapturedToken);

        await modem.DisposeAsync();
    }

    [Fact]
    public async Task DisposeAsync_ClosesTheKissStreamItOpened()
    {
        TrackingStream stream = new();
        ModemFactory factory = TestModemFactory.Create(
            kissStreamFactory: new StubKissStreamFactory(stream));

        IPortModem modem = await factory.CreateAsync(
            CreatePort(new KissSettings
            {
                Transport = new TcpKissTransportSettings { Host = "127.0.0.1", Port = 8001 }
            }),
            TestContext.Current.CancellationToken);

        await modem.DisposeAsync();

        Assert.True(stream.WasDisposed);

        // Disposal is idempotent so a stop-then-dispose sequence cannot throw.
        await modem.DisposeAsync();
    }

    private static PortConfig CreatePort(IPortTypeSettings? settings) => new()
    {
        Id = Guid.NewGuid(),
        Name = "Test port",
        TypeSettings = settings
    };

    /// <summary>
    /// A settings implementation the production application never registers, used
    /// to prove an unknown transport family is reported instead of ignored.
    /// </summary>
    private sealed class UnregisteredPortTypeSettings : IPortTypeSettings
    {
        public IPortTypeSettings Clone() => new UnregisteredPortTypeSettings();
    }

    private sealed class StubKissStreamFactory(Stream stream) : IKissStreamFactory
    {
        public System.Collections.Generic.IReadOnlyCollection<Type> SupportedTransports { get; } =
            [typeof(TcpKissTransportSettings)];

        public Task<Stream> OpenAsync(
            KissSettings settings,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(stream);
    }

    private sealed class RecordingConnector : IKissStreamConnector
    {
        public Type SettingsType => typeof(TcpKissTransportSettings);

        public bool IsSupported => true;

        public CancellationToken CapturedToken { get; private set; }

        public Task<Stream> ConnectAsync(
            IKissTransportSettings settings,
            CancellationToken cancellationToken = default)
        {
            CapturedToken = cancellationToken;
            return Task.FromResult<Stream>(new MemoryStream());
        }
    }

    private sealed class TrackingStream : MemoryStream
    {
        public bool WasDisposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            WasDisposed = true;
            base.Dispose(disposing);
        }
    }
}
