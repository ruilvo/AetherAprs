// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Threading;
using System.Threading.Tasks;

namespace AetherAprs.Services.Bluetooth;

/// <summary>
/// Writes individual ATT-sized chunks to a Bluetooth LE characteristic.
/// </summary>
/// <remarks>
/// This is the platform seam for <see cref="BluetoothLeFrameWriter"/>. An adapter
/// implements one write attempt and reports what the stack said; slicing, pacing
/// and retries are decided by the frame writer so they can be tested without a
/// device.
/// </remarks>
public interface IBluetoothLeChunkWriter
{
    /// <summary>
    /// Whether the characteristic accepts write-without-response, which has no
    /// ATT-level flow control and therefore needs pacing between chunks.
    /// </summary>
    bool SupportsWriteWithoutResponse { get; }

    /// <summary>
    /// The usable payload size for one write, derived from the negotiated ATT MTU.
    /// </summary>
    int PayloadSize { get; }

    /// <summary>
    /// Attempts one write. The implementation must not retry: a transient outcome
    /// is returned so the frame writer can decide.
    /// </summary>
    /// <param name="chunk">The bytes to write, at most <see cref="PayloadSize"/> long.</param>
    /// <param name="withoutResponse">Whether to use write-without-response.</param>
    /// <param name="cancellationToken">Cancels the wait for the stack's answer.</param>
    /// <returns>What the stack reported for this attempt.</returns>
    Task<GattWriteOutcome> WriteChunkAsync(
        ReadOnlyMemory<byte> chunk,
        bool withoutResponse,
        CancellationToken cancellationToken);
}
