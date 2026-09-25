// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Configuration;
using System;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace AetherAprs.Transports.Kiss;

/// <summary>
/// Opens a TCP stream for KISS TNCs that expose a TCP server.
/// </summary>
public sealed class TcpKissStreamConnector : IKissStreamConnector
{
    /// <inheritdoc />
    public Type SettingsType => typeof(TcpKissTransportSettings);

    /// <inheritdoc />
    public bool IsSupported => true;

    /// <inheritdoc />
    public async Task<Stream> ConnectAsync(IKissTransportSettings settings, CancellationToken cancellationToken = default)
    {
        if (settings is not TcpKissTransportSettings tcpSettings)
        {
            throw new ArgumentException(
                $"Expected {nameof(TcpKissTransportSettings)}, got {settings?.GetType().Name ?? "null"}.",
                nameof(settings));
        }

        var client = new TcpClient();
        try
        {
            await client.ConnectAsync(tcpSettings.Host, tcpSettings.Port, cancellationToken).ConfigureAwait(false);
            return new TcpClientNetworkStream(client);
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    /// <summary>
    /// NetworkStream wrapper that owns the underlying <see cref="TcpClient"/>.
    /// </summary>
    private sealed class TcpClientNetworkStream(TcpClient client) : Stream
    {
        private readonly NetworkStream _stream = client.GetStream();
        private bool _disposed;

        public override bool CanRead => _stream.CanRead;

        public override bool CanSeek => _stream.CanSeek;

        public override bool CanWrite => _stream.CanWrite;

        public override long Length => _stream.Length;

        public override long Position
        {
            get => _stream.Position;
            set => _stream.Position = value;
        }

        public override void Flush() => _stream.Flush();

        public override Task FlushAsync(CancellationToken cancellationToken) =>
            _stream.FlushAsync(cancellationToken);

        public override int Read(byte[] buffer, int offset, int count) =>
            _stream.Read(buffer, offset, count);

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            _stream.ReadAsync(buffer, offset, count, cancellationToken);

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            _stream.ReadAsync(buffer, cancellationToken);

        public override long Seek(long offset, SeekOrigin origin) =>
            _stream.Seek(offset, origin);

        public override void SetLength(long value) =>
            _stream.SetLength(value);

        public override void Write(byte[] buffer, int offset, int count) =>
            _stream.Write(buffer, offset, count);

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            _stream.WriteAsync(buffer, offset, count, cancellationToken);

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) =>
            _stream.WriteAsync(buffer, cancellationToken);

        protected override void Dispose(bool disposing)
        {
            if (_disposed)
            {
                return;
            }

            if (disposing)
            {
                _stream.Dispose();
                client.Dispose();
            }

            _disposed = true;
            base.Dispose(disposing);
        }
    }
}