// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Services.Platform;
using System;
using System.Threading;

namespace AetherAprs.Android.Services.Platform;

/// <summary>Why a chunked audio transfer stopped.</summary>
internal enum AudioTransferStatus
{
    /// <summary>The whole requested length was transferred.</summary>
    Completed,

    /// <summary>The device accepted or produced zero bytes, so no progress is possible.</summary>
    ZeroLength,

    /// <summary>The device reported an error with a negative count.</summary>
    Failed,

    /// <summary>The caller aborted the transfer through the cancellation token.</summary>
    Aborted,

    /// <summary>The audio route or focus stopped permitting transmission.</summary>
    Blocked
}

/// <summary>Outcome of one chunked audio transfer.</summary>
/// <param name="Transferred">Number of items transferred before the loop stopped.</param>
/// <param name="Status">Why the loop stopped.</param>
/// <param name="BlockReason">
/// Why transmission was refused, when <paramref name="Status"/> is
/// <see cref="AudioTransferStatus.Blocked"/>.
/// </param>
internal readonly record struct AudioTransferResult(
    int Transferred,
    AudioTransferStatus Status,
    AudioTransmitBlockReason BlockReason = AudioTransmitBlockReason.None)
{
    /// <summary>Gets whether every requested item was transferred.</summary>
    public bool IsComplete => Status == AudioTransferStatus.Completed;

    /// <summary>Gets whether the route stopped permitting transmission mid-frame.</summary>
    public bool IsBlocked => Status == AudioTransferStatus.Blocked;
}

/// <summary>
/// Drives a chunked audio transfer to completion. Kept free of Android types so
/// the partial-write and short-read behaviour can be unit-tested.
/// </summary>
/// <remarks>
/// <c>AudioTrack.Write</c> may write fewer samples than requested and
/// <c>AudioRecord.Read</c> may return fewer samples than the buffer can hold; a
/// single unchecked call silently truncates a modulated frame. The write loop
/// therefore retries until the whole buffer is accepted, and the read loop
/// accumulates a chunk until it fills the buffer or the source reports that it
/// has nothing more for this chunk.
/// </remarks>
internal static class AudioWriteLoop
{
    /// <summary>Transfers one chunk at the given offset and returns the item count.</summary>
    /// <param name="offset">Index into the caller's buffer to start at.</param>
    /// <param name="count">Maximum number of items to transfer.</param>
    /// <returns>
    /// Number of items transferred: negative on a device error, zero when no
    /// progress was made, otherwise a positive count no greater than
    /// <paramref name="count"/>.
    /// </returns>
    public delegate int ChunkTransfer(int offset, int count);

    /// <summary>
    /// Checks, immediately before a chunk is transferred, whether transmission is
    /// still permitted. Returning anything but
    /// <see cref="AudioTransmitBlockReason.None"/> stops the transfer.
    /// </summary>
    /// <returns>The reason transmission is refused, or None when it may continue.</returns>
    public delegate AudioTransmitBlockReason TransmitGuard();

    /// <summary>
    /// Writes <paramref name="length"/> items through <paramref name="write"/>,
    /// retrying partial writes until the buffer is exhausted.
    /// </summary>
    /// <param name="length">Number of items in the caller's buffer.</param>
    /// <param name="write">Writes a chunk at an offset.</param>
    /// <param name="cancellationToken">Aborts the loop between chunks.</param>
    public static AudioTransferResult WriteAll(
        int length,
        ChunkTransfer write,
        CancellationToken cancellationToken = default) =>
        WriteAll(length, write, guard: null, cancellationToken);

    /// <summary>
    /// Writes <paramref name="length"/> items through <paramref name="write"/>,
    /// revalidating transmission before each chunk.
    /// </summary>
    /// <param name="length">Number of items in the caller's buffer.</param>
    /// <param name="write">Writes a chunk at an offset.</param>
    /// <param name="guard">
    /// Revalidates the audio route before each chunk. A frame can take long enough
    /// that focus is lost or the headset is unplugged while it is going out, so the
    /// decision cannot be made once at the start.
    /// </param>
    /// <param name="cancellationToken">Aborts the loop between chunks.</param>
    public static AudioTransferResult WriteAll(
        int length,
        ChunkTransfer write,
        TransmitGuard? guard,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        ArgumentNullException.ThrowIfNull(write);

        int offset = 0;
        while (offset < length)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return new AudioTransferResult(offset, AudioTransferStatus.Aborted);
            }

            if (guard is not null)
            {
                AudioTransmitBlockReason blockReason = guard();
                if (blockReason != AudioTransmitBlockReason.None)
                {
                    return new AudioTransferResult(offset, AudioTransferStatus.Blocked, blockReason);
                }
            }

            int written = write(offset, length - offset);
            if (written < 0)
            {
                return new AudioTransferResult(offset, AudioTransferStatus.Failed);
            }

            if (written == 0)
            {
                return new AudioTransferResult(offset, AudioTransferStatus.ZeroLength);
            }

            // A buggy writer must not make the loop overrun the buffer or spin.
            offset += Math.Min(written, length - offset);
        }

        return new AudioTransferResult(offset, AudioTransferStatus.Completed);
    }

    /// <summary>
    /// Reads up to <paramref name="capacity"/> items through <paramref name="read"/>,
    /// accumulating short reads until the buffer fills or the source returns zero.
    /// </summary>
    /// <param name="capacity">Size of the caller's buffer.</param>
    /// <param name="read">Reads a chunk at an offset.</param>
    /// <param name="cancellationToken">Aborts the loop between chunks.</param>
    /// <remarks>
    /// A zero read after a partial chunk ends the chunk so the caller can forward
    /// the samples it already has instead of blocking for the remainder.
    /// </remarks>
    public static AudioTransferResult ReadUpTo(
        int capacity,
        ChunkTransfer read,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(capacity);
        ArgumentNullException.ThrowIfNull(read);

        int offset = 0;
        while (offset < capacity)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return new AudioTransferResult(offset, AudioTransferStatus.Aborted);
            }

            int readCount = read(offset, capacity - offset);
            if (readCount < 0)
            {
                return new AudioTransferResult(offset, AudioTransferStatus.Failed);
            }

            if (readCount == 0)
            {
                return offset == 0
                    ? new AudioTransferResult(0, AudioTransferStatus.ZeroLength)
                    : new AudioTransferResult(offset, AudioTransferStatus.Completed);
            }

            offset += Math.Min(readCount, capacity - offset);
        }

        return new AudioTransferResult(offset, AudioTransferStatus.Completed);
    }
}
