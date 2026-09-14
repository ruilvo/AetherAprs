// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using Android.Bluetooth;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace AetherAprs.Android.Services;

/// <summary>
/// Duplex stream over a connected Bluetooth Classic RFCOMM socket.
/// Owns and disposes the underlying <see cref="BluetoothSocket"/>.
/// </summary>
internal sealed class BluetoothSocketDuplexStream : Stream
{
    private readonly BluetoothSocket _socket;
    private readonly Stream _input;
    private readonly Stream _output;
    private bool _disposed;

    public BluetoothSocketDuplexStream(BluetoothSocket socket)
    {
        _socket = socket ?? throw new ArgumentNullException(nameof(socket));
        _input = socket.InputStream ?? throw new InvalidOperationException("Bluetooth socket input stream is unavailable.");
        _output = socket.OutputStream ?? throw new InvalidOperationException("Bluetooth socket output stream is unavailable.");
    }

    public override bool CanRead => !_disposed;

    public override bool CanSeek => false;

    public override bool CanWrite => !_disposed;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Flush() => _output.Flush();

    public override Task FlushAsync(CancellationToken cancellationToken) => _output.FlushAsync(cancellationToken);

    public override int Read(byte[] buffer, int offset, int count) => _input.Read(buffer, offset, count);

    public override int Read(Span<byte> buffer) => _input.Read(buffer);

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        _input.ReadAsync(buffer, offset, count, cancellationToken);

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
        _input.ReadAsync(buffer, cancellationToken);

    public override void Write(byte[] buffer, int offset, int count) => _output.Write(buffer, offset, count);

    public override void Write(ReadOnlySpan<byte> buffer) => _output.Write(buffer);

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        _output.WriteAsync(buffer, offset, count, cancellationToken);

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) =>
        _output.WriteAsync(buffer, cancellationToken);

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (disposing)
        {
            try
            {
                _input.Dispose();
            }
            catch
            {
                // Ignore dispose failures from native streams.
            }

            try
            {
                _output.Dispose();
            }
            catch
            {
                // Ignore dispose failures from native streams.
            }

            try
            {
                if (_socket.IsConnected)
                {
                    _socket.Close();
                }
            }
            catch
            {
                // Ignore close failures during dispose.
            }

            _socket.Dispose();
        }

        base.Dispose(disposing);
    }
}