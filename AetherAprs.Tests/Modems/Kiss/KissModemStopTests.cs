// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using AetherAprs.Modems.Kiss;

using Xunit;

namespace AetherAprs.Tests.Modems.Kiss;

/// <summary>
/// Regression tests for the KISS modem stop path.
/// <para>
/// A platform stream can implement <see cref="Stream.ReadAsync(Memory{byte}, CancellationToken)"/>
/// as a blocking call that ignores its cancellation token (the Java
/// <c>InputStream</c> behind a Bluetooth Classic socket does exactly that). Stopping
/// or disposing the modem must therefore close the stream <em>before</em> waiting
/// for the read loop; otherwise the awaited read task never completes and the
/// operation hangs forever.
/// </para>
/// </summary>
public sealed class KissModemStopTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(3);

    [Fact]
    public async Task StopAsync_ReadIgnoresCancellation_CompletesWithinBoundedTime()
    {
        using DisposalUnblockedReadStream stream = new();
        await using KissModem modem = new(stream);

        modem.Start();
        await stream.ReadStarted.WaitAsync(TestTimeout, TestContext.Current.CancellationToken);

        await modem.StopAsync().WaitAsync(TestTimeout, TestContext.Current.CancellationToken);

        Assert.True(stream.IsDisposed, "StopAsync must close the stream to unblock the read loop.");
    }

    [Fact]
    public async Task DisposeAsync_ReadIgnoresCancellation_CompletesWithinBoundedTime()
    {
        using DisposalUnblockedReadStream stream = new();
        KissModem modem = new(stream);

        modem.Start();
        await stream.ReadStarted.WaitAsync(TestTimeout, TestContext.Current.CancellationToken);

        await modem.DisposeAsync().AsTask().WaitAsync(TestTimeout, TestContext.Current.CancellationToken);

        Assert.True(stream.IsDisposed, "DisposeAsync must close the stream to unblock the read loop.");
    }

    /// <summary>
    /// Duplex stream whose read blocks until the stream is disposed and deliberately
    /// ignores the supplied cancellation token.
    /// </summary>
    private sealed class DisposalUnblockedReadStream : Stream
    {
        private readonly TaskCompletionSource<bool> _readStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private readonly TaskCompletionSource<bool> _disposed =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task ReadStarted => _readStarted.Task;

        public bool IsDisposed => _disposed.Task.IsCompleted;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            // Deliberately ignores cancellationToken; only disposal releases the read.
            _readStarted.TrySetResult(true);
            await _disposed.Task.ConfigureAwait(false);
            return 0;
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            ReadAsync(new Memory<byte>(buffer, offset, count)).AsTask().GetAwaiter().GetResult();

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count)
        {
        }

        protected override void Dispose(bool disposing)
        {
            _disposed.TrySetResult(true);
            base.Dispose(disposing);
        }
    }
}
