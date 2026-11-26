// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Threading;
using System.Threading.Channels;

namespace AetherAprs.Services.Bluetooth;

/// <summary>
/// Bounded hand-off between the platform scan callback and the consumer that
/// enumerates advertisements.
/// </summary>
/// <remarks>
/// The platform delivers advertisements on a binder thread that must not block,
/// so an unbounded queue would let a slow consumer grow memory for as long as the
/// scan runs. The buffer keeps the newest sightings and drops the oldest ones
/// instead: the device picker exists to show what is around now, not everything
/// that was ever seen. Dropped advertisements are counted so a scan that outruns
/// its consumer can be noticed rather than silently losing entries.
/// </remarks>
public sealed class BluetoothAdvertisementBuffer
{
    /// <summary>
    /// The number of advertisements held when the caller does not choose a size.
    /// A picker redraws far slower than a busy room produces advertisements, so
    /// the buffer is deliberately small.
    /// </summary>
    public const int DefaultCapacity = 256;

    private readonly Channel<BluetoothLeAdvertisement> _channel;
    private long _dropped;

    /// <summary>
    /// Creates a buffer that holds at most <paramref name="capacity"/>
    /// advertisements.
    /// </summary>
    /// <param name="capacity">The maximum number of retained advertisements.</param>
    public BluetoothAdvertisementBuffer(int capacity = DefaultCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);

        Capacity = capacity;
        _channel = Channel.CreateBounded<BluetoothLeAdvertisement>(new BoundedChannelOptions(capacity)
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
            FullMode = BoundedChannelFullMode.DropOldest
        });
    }

    /// <summary>The maximum number of advertisements the buffer retains.</summary>
    public int Capacity { get; }

    /// <summary>The advertisements waiting for the consumer, oldest first.</summary>
    public ChannelReader<BluetoothLeAdvertisement> Reader => _channel.Reader;

    /// <summary>
    /// The number of advertisements evicted because the buffer was full. The
    /// count is advisory: it is sampled before writing, so a concurrent write can
    /// make it lag by one.
    /// </summary>
    public long DroppedCount => Interlocked.Read(ref _dropped);

    /// <summary>
    /// Offers one advertisement to the consumer.
    /// </summary>
    /// <param name="advertisement">The advertisement to publish.</param>
    /// <returns>
    /// <see langword="true"/> when the advertisement was retained or replaced the
    /// oldest entry; <see langword="false"/> when the buffer is already complete.
    /// </returns>
    public bool Publish(BluetoothLeAdvertisement advertisement)
    {
        ArgumentNullException.ThrowIfNull(advertisement);

        // DropOldest still reports success, so the eviction has to be observed
        // before the write to be counted at all.
        if (_channel.Reader.CanCount && _channel.Reader.Count >= Capacity)
        {
            Interlocked.Increment(ref _dropped);
        }

        return _channel.Writer.TryWrite(advertisement);
    }

    /// <summary>
    /// Ends the buffer, optionally with the failure that stopped the scan.
    /// </summary>
    /// <param name="error">
    /// The scan failure, or <see langword="null"/> for a normal completion.
    /// </param>
    public void Complete(Exception? error = null) => _channel.Writer.TryComplete(error);
}
