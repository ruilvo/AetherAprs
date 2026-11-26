// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace AetherAprs.Tests.Modems.Kiss;

/// <summary>
/// A duplex stream that behaves like the Java input stream behind a Bluetooth
/// Classic RFCOMM socket: a read blocks until the socket is closed and never
/// observes the cancellation token.
/// </summary>
/// <remarks>
/// This is the case the KISS teardown exists for. A double that honours
/// cancellation would pass even if the modem awaited the receive loop before
/// closing the stream, so the ordering would go untested. The recorded
/// <see cref="Events"/> make that ordering assertable: a correct stop records
/// <c>dispose</c> before <c>unwound</c>.
/// </remarks>
/// <remarks>
/// Creates the double.
/// </remarks>
/// <param name="unwindsOnClose">
/// <see langword="true"/> closes the read like a real socket does;
/// <see langword="false"/> models a native read that stays wedged even after
/// the handle is closed, which the modem's bounded wait must survive.
/// </param>
internal sealed class BlockingRfcommStream(bool unwindsOnClose = true) : Stream
{
    private static readonly Task NeverCompletes = new TaskCompletionSource().Task;

    private readonly bool _unwindsOnClose = unwindsOnClose;
    private readonly Lock _gate = new();
    private readonly List<string> _events = [];
    private readonly TaskCompletionSource _readStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _unblocked = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _readFinished = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _disposed;

    /// <summary>The call order recorded by the double, oldest first.</summary>
    public IReadOnlyList<string> Events
    {
        get
        {
            lock (_gate)
            {
                return [.. _events];
            }
        }
    }

    public override bool CanRead => Volatile.Read(ref _disposed) == 0;

    public override bool CanSeek => false;

    public override bool CanWrite => Volatile.Read(ref _disposed) == 0;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    /// <summary>Completes once the receive loop has entered a read.</summary>
    public Task WaitForReadAsync(CancellationToken cancellationToken) =>
        _readStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);

    /// <summary>Completes once the blocked read has returned.</summary>
    public Task WaitForReadToFinishAsync(CancellationToken cancellationToken) =>
        _readFinished.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);

    public override void Flush()
    {
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        Record("read");
        _readStarted.TrySetResult();
        _unblocked.Task.GetAwaiter().GetResult();
        Record("unwound");
        _readFinished.TrySetResult();
        return 0;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        Record("read");
        _readStarted.TrySetResult();

        // Deliberately ignores cancellationToken: that is the behaviour under test.
        await _unblocked.Task.ConfigureAwait(false);

        if (!_unwindsOnClose)
        {
            await NeverCompletes.ConfigureAwait(false);
        }

        Record("unwound");
        _readFinished.TrySetResult();
        return 0;
    }

    public override void Write(byte[] buffer, int offset, int count) => Record("write");

    public override void Write(ReadOnlySpan<byte> buffer) => Record("write");

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        Record("write");
        return ValueTask.CompletedTask;
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            Record("dispose");
            _unblocked.TrySetResult();
        }

        base.Dispose(disposing);
    }

    private void Record(string entry)
    {
        lock (_gate)
        {
            _events.Add(entry);
        }
    }
}
