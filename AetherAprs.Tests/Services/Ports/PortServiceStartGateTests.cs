// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AetherAprs.Configuration;
using AetherAprs.Models.Aprs.Packets;
using AetherAprs.Services.Contracts;
using AetherAprs.Services.Packets;
using AetherAprs.Services.Platform;
using AetherAprs.Services.Ports;
using AetherAprs.Tests.Helpers;
using AetherAprs.Transports.Kiss;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace AetherAprs.Tests.Services.Ports;

/// <summary>
/// Regression tests for the per-port start gate (audit C-01): two concurrent
/// starts for one port must open exactly one transport stream.
/// </summary>
public sealed class PortServiceStartGateTests
{
    [Fact]
    public async Task ConcurrentStarts_OpenOnlyOneStream()
    {
        var portId = Guid.NewGuid();
        var port = new PortConfig
        {
            Id = portId,
            Name = "KISS",
            IsEnabled = false,
            IsTx = true,
            TypeSettings = new KissSettings
            {
                Transport = new TcpKissTransportSettings { Host = "127.0.0.1", Port = 8001 }
            }
        };
        using var db = TempAppDatabase.Create(port);
        var factory = new BlockingKissStreamFactory();
        await using var service = CreateService(db, factory);
        await service.InitializeAsync(TestContext.Current.CancellationToken);

        // Both callers race to start the same port while the transport is still opening.
        var first = service.SetPortEnabledAsync(portId, true);
        var second = service.SetPortEnabledAsync(portId, true);

        await factory.Entered.Task.WaitAsync(
            TimeSpan.FromSeconds(3),
            TestContext.Current.CancellationToken);
        factory.Release();

        await Task.WhenAll(
            first.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken),
            second.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));

        Assert.Equal(1, factory.OpenCount);

        await service.StopAllPortsAsync();
    }

    private static PortService CreateService(TempAppDatabase db, IKissStreamFactory kissStreamFactory)
    {
        var digipeater = Substitute.For<IDigipeaterService>();
        digipeater.GetDigipeatTargets(
            Arg.Any<AprsPacket>(),
            Arg.Any<Guid>(),
            Arg.Any<bool>(),
            Arg.Any<IReadOnlyList<PortInfo>>())
            .Returns([]);

        return new PortService(
            StoreFactory.CreatePortRegistry(db),
            TestModemFactory.Create(kissStreamFactory: kissStreamFactory),
            NullLogger<PortService>.Instance,
            Substitute.For<IPacketStorageService>(),
            Substitute.For<IKeepAliveService>(),
            digipeater);
    }

    private sealed class BlockingKissStreamFactory : IKissStreamFactory
    {
        private readonly TaskCompletionSource<bool> _gate =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private int _openCount;

        public IReadOnlyCollection<Type> SupportedTransports { get; } =
            [typeof(TcpKissTransportSettings)];

        /// <summary>Signalled once the first open has been requested.</summary>
        public TaskCompletionSource<bool> Entered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int OpenCount => Volatile.Read(ref _openCount);

        public void Release() => _gate.TrySetResult(true);

        public async Task<Stream> OpenAsync(
            KissSettings settings,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _openCount);
            Entered.TrySetResult(true);
            await _gate.Task.WaitAsync(cancellationToken);
            return new IdleStream();
        }
    }

    private sealed class IdleStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => 0;

        public override long Position
        {
            get => 0;
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => 0;

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            try
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return 0;
            }

            return 0;
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();
    }

}
