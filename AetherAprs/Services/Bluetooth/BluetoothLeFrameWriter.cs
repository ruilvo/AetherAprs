// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace AetherAprs.Services.Bluetooth;

/// <summary>
/// Writes a whole frame over a Bluetooth LE characteristic, sliced to the
/// negotiated payload size with bounded retries for transient stack failures.
/// </summary>
/// <remarks>
/// A KISS frame is usually larger than the ATT MTU. Android's stack truncates an
/// over-MTU write and still reports success, so the frame must be sliced here.
/// Write-without-response has no ATT-level flow control, so consecutive chunks are
/// paced; write-with-response already serializes on the stack's acknowledgement.
/// Retries are bounded because a stack that keeps answering busy is a connection
/// problem, not a timing problem, and the caller needs the failure to reconnect.
/// </remarks>
public sealed class BluetoothLeFrameWriter
{
    /// <summary>Total attempts per chunk, including the first one.</summary>
    public const int DefaultMaxAttempts = 4;

    /// <summary>Base delay between retries; multiplied by the attempt number.</summary>
    public static readonly TimeSpan DefaultRetryDelay = TimeSpan.FromMilliseconds(50);

    /// <summary>Pacing between write-without-response chunks.</summary>
    public static readonly TimeSpan DefaultPacingDelay = TimeSpan.FromMilliseconds(20);

    private readonly IBluetoothLeChunkWriter _chunkWriter;
    private readonly int _maxAttempts;
    private readonly TimeSpan _retryDelay;
    private readonly TimeSpan _pacingDelay;

    /// <summary>
    /// Creates a writer over <paramref name="chunkWriter"/>.
    /// </summary>
    /// <param name="chunkWriter">The platform seam that performs one write.</param>
    /// <param name="maxAttempts">Total attempts per chunk, including the first.</param>
    /// <param name="retryDelay">
    /// Base retry delay; the candidate delay is this value times the attempt number.
    /// </param>
    /// <param name="pacingDelay">
    /// Delay between write-without-response chunks when more data follows.
    /// </param>
    public BluetoothLeFrameWriter(
        IBluetoothLeChunkWriter chunkWriter,
        int maxAttempts = DefaultMaxAttempts,
        TimeSpan? retryDelay = null,
        TimeSpan? pacingDelay = null)
    {
        _chunkWriter = chunkWriter ?? throw new ArgumentNullException(nameof(chunkWriter));
        ArgumentOutOfRangeException.ThrowIfLessThan(maxAttempts, 1);
        _maxAttempts = maxAttempts;
        _retryDelay = retryDelay ?? DefaultRetryDelay;
        _pacingDelay = pacingDelay ?? DefaultPacingDelay;
    }

    /// <summary>
    /// Writes <paramref name="frame"/> to the characteristic, chunk by chunk.
    /// </summary>
    /// <param name="frame">The bytes to write.</param>
    /// <param name="cancellationToken">Cancels a wait between attempts or chunks.</param>
    /// <exception cref="IOException">
    /// A chunk kept failing after <see cref="DefaultMaxAttempts"/> attempts.
    /// </exception>
    public async Task WriteAsync(
        ReadOnlyMemory<byte> frame,
        CancellationToken cancellationToken = default)
    {
        if (frame.IsEmpty)
        {
            return;
        }

        int payloadSize = _chunkWriter.PayloadSize;
        if (payloadSize <= 0)
        {
            throw new InvalidOperationException("The characteristic reported no usable payload size.");
        }

        bool withoutResponse = _chunkWriter.SupportsWriteWithoutResponse;

        for (int offset = 0; offset < frame.Length; offset += payloadSize)
        {
            ReadOnlyMemory<byte> chunk = frame.Slice(offset, Math.Min(payloadSize, frame.Length - offset));
            await WriteChunkAsync(chunk, withoutResponse, cancellationToken).ConfigureAwait(false);

            if (withoutResponse && offset + payloadSize < frame.Length)
            {
                await Task.Delay(_pacingDelay, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task WriteChunkAsync(
        ReadOnlyMemory<byte> chunk,
        bool withoutResponse,
        CancellationToken cancellationToken)
    {
        for (int attempt = 1; ; attempt++)
        {
            GattWriteOutcome outcome = await _chunkWriter
                .WriteChunkAsync(chunk, withoutResponse, cancellationToken)
                .ConfigureAwait(false);

            if (outcome == GattWriteOutcome.Success)
            {
                return;
            }

            if (IsRetryable(outcome) && attempt < _maxAttempts)
            {
                await Task.Delay(_retryDelay * attempt, cancellationToken).ConfigureAwait(false);
                continue;
            }

            throw new IOException(withoutResponse
                ? $"BLE characteristic write (no response) failed with outcome {outcome}."
                : $"BLE characteristic write failed with outcome {outcome}.");
        }
    }

    private static bool IsRetryable(GattWriteOutcome outcome) =>
        outcome is GattWriteOutcome.Busy or GattWriteOutcome.ConnectionError;
}
