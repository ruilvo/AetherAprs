// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
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
    private readonly ILogger<KissModem> _logger;
    private readonly List<byte> _accumulator = [];

    /// <summary>Serializes concurrent send attempts so KISS frames cannot interleave on the wire.</summary>
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    /// <summary>Guards the start/stop lifecycle state so concurrent Start/Stop calls cannot race.</summary>
    private readonly object _lifecycleLock = new();

    /// <summary>
    /// Maximum accumulated frame size before the stream is treated as hostile or
    /// misconfigured. Well-formed KISS/AX.25 frames are far smaller (a maximum UI
    /// frame is ~330 bytes); anything above this means no FEND will ever arrive.
    /// </summary>
    private const int MaxFrameLength = 4096;

    private Task? _readTask;
    private CancellationTokenSource? _readCts;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of <see cref="KissModem"/>.
    /// </summary>
    /// <param name="stream">The duplex stream to communicate over. Must be readable and writable.</param>
    /// <param name="loggerFactory">Optional logger factory.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="stream"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="stream"/> does not support both read and write.</exception>
    public KissModem(Stream stream, ILoggerFactory? loggerFactory = null)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (!stream.CanRead || !stream.CanWrite)
        {
            throw new ArgumentException("Stream must support both reading and writing.", nameof(stream));
        }

        _stream = stream;
        _logger = loggerFactory?.CreateLogger<KissModem>() ?? NullLogger<KissModem>.Instance;
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

        lock (_lifecycleLock)
        {
            if (_readTask is not null)
            {
                throw new InvalidOperationException("Modem is already running.");
            }

            // A fresh CTS per start: after StopAsync the previous token is
            // cancelled and would silently kill the new read loop.
            _readCts?.Dispose();
            _readCts = new CancellationTokenSource();
            _readTask = ReadLoopAsync(_readCts.Token);
        }

        _logger.LogInformation("KISS modem started.");
    }

    /// <summary>
    /// Stops the background receive loop.
    /// </summary>
    /// <returns>A task that completes when the receive loop has stopped.</returns>
    public async Task StopAsync()
    {
        Task? readTask;
        CancellationTokenSource? readCts;

        lock (_lifecycleLock)
        {
            readTask = _readTask;
            readCts = _readCts;
            _readTask = null;
        }

        if (readTask is null)
        {
            return;
        }

        if (readCts is not null)
        {
            await readCts.CancelAsync().ConfigureAwait(false);
        }

        try
        {
            await readTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected on cancellation.
        }

        _logger.LogInformation("KISS modem stopped.");
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
        await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _stream.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
            await _stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        var readCts = _readCts;
        if (readCts is not null)
        {
            await readCts.CancelAsync().ConfigureAwait(false);
        }

        var readTask = _readTask;
        if (readTask is not null)
        {
            try
            {
                await readTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected on cancellation.
            }
        }

        // Wait for in-flight sends before tearing down the stream.
        await _sendLock.WaitAsync().ConfigureAwait(false);
        try
        {
            _stream.Dispose();
        }
        finally
        {
            _sendLock.Release();
        }

        _sendLock.Dispose();
        readCts?.Dispose();
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
                        if (_accumulator.Count >= MaxFrameLength)
                        {
                            // Unbounded growth means the stream never delivers a FEND;
                            // discard and resynchronize on the next FEND.
                            OnReceiveError(new InvalidOperationException(
                                $"KISS frame exceeds maximum length of {MaxFrameLength} bytes; discarding."));
                            _accumulator.Clear();
                            inFrame = false;
                        }
                        else
                        {
                            _accumulator.Add(b);
                        }
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
            // The command byte is transmitted unescaped per the KISS spec;
            // only the payload is escaped.
            var data = _accumulator.Count > 1
                ? KissSerializer.UnescapeData([.. _accumulator.GetRange(1, _accumulator.Count - 1)])
                : [];
            var frame = new KissFrame(_accumulator[0], data);
            _logger.LogDebug(
                "KISS frame received: Command={Command}, Port={Port}, DataLength={Length}",
                frame.CommandType,
                frame.Port,
                frame.Data.Length);
            FrameReceived?.Invoke(this, frame);
        }
        catch (Exception ex)
        {
            OnReceiveError(ex);
        }
    }

    private void OnReceiveError(Exception exception)
    {
        _logger.LogWarning(exception, "KISS modem receive error.");
        ReceiveError?.Invoke(this, exception);
    }
}