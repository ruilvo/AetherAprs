// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace AetherAprs.Modems.Kiss;

/// <summary>
/// Async modem that sends and receives KISS frames over a Stream.
/// Implements the KISS framing protocol on top of any duplex byte stream
/// (serial port, TCP stream, etc.).
/// </summary>
public sealed class KissModem : IAsyncDisposable
{
    private readonly Stream _stream;
    private readonly CancellationTokenSource _readCts = new();
    private readonly List<byte> _accumulator = [];
    private Task? _readTask;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of <see cref="KissModem"/>.
    /// </summary>
    /// <param name="stream">The duplex stream to communicate over. Must be readable and writable.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="stream"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="stream"/> does not support both read and write.</exception>
    public KissModem(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (!stream.CanRead || !stream.CanWrite)
        {
            throw new ArgumentException("Stream must support both reading and writing.", nameof(stream));
        }

        _stream = stream;
    }

    /// <summary>
    /// Raised when a complete and valid KISS frame has been received.
    /// </summary>
    public event EventHandler<KissFrame>? FrameReceived;

    /// <summary>
    /// Raised when an error occurs during frame reception.
    /// </summary>
    public event EventHandler<Exception>? ReceiveError;

    /// <summary>
    /// Starts the background receive loop.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown if the modem is already running.</exception>
    /// <exception cref="ObjectDisposedException">Thrown if the modem has been disposed.</exception>
    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_readTask is not null)
        {
            throw new InvalidOperationException("Modem is already running.");
        }

        _readTask = ReadLoopAsync(_readCts.Token);
    }

    /// <summary>
    /// Stops the background receive loop.
    /// </summary>
    /// <returns>A task that completes when the receive loop has stopped.</returns>
    public async Task StopAsync()
    {
        if (_readTask is null)
        {
            return;
        }

        await _readCts.CancelAsync();

        try
        {
            await _readTask;
        }
        catch (OperationCanceledException)
        {
            // Expected on cancellation.
        }

        _readTask = null;
    }

    /// <summary>
    /// Encodes and sends a KISS frame over the stream.
    /// </summary>
    /// <param name="frame">The frame to send.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>A task that completes when the frame has been written to the stream.</returns>
    public async Task SendAsync(KissFrame frame, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var buffer = KissSerializer.EncodeFrame(frame);
        await _stream.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
        await _stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        await _readCts.CancelAsync();
        _readCts.Dispose();

        if (_readTask is not null)
        {
            try
            {
                await _readTask;
            }
            catch (OperationCanceledException)
            {
                // Expected on cancellation.
            }
        }

        _stream.Dispose();
    }

    private async Task ReadLoopAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];
        var inFrame = false;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var bytesRead = await _stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);

                if (bytesRead == 0)
                {
                    // Stream closed by remote end.
                    break;
                }

                for (var i = 0; i < bytesRead; i++)
                {
                    var b = buffer[i];

                    if (b == KissConstants.FEND)
                    {
                        if (inFrame)
                        {
                            // End of frame: decode what we accumulated.
                            if (_accumulator.Count > 0)
                            {
                                TryDispatchFrame();
                            }

                            _accumulator.Clear();
                            inFrame = false;
                        }
                        else
                        {
                            // Start of frame.
                            _accumulator.Clear();
                            inFrame = true;
                        }
                    }
                    else if (inFrame)
                    {
                        _accumulator.Add(b);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Shutdown requested.
        }
        catch (Exception ex)
        {
            OnReceiveError(ex);
        }
    }

    private void TryDispatchFrame()
    {
        try
        {
            var unescaped = KissSerializer.UnescapeData([.. _accumulator]);
            var frame = KissSerializer.DecodeFrame(unescaped);
            FrameReceived?.Invoke(this, frame);
        }
        catch (Exception ex)
        {
            OnReceiveError(ex);
        }
    }

    private void OnReceiveError(Exception exception)
    {
        ReceiveError?.Invoke(this, exception);
    }
}