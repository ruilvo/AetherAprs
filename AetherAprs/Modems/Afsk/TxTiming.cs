// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;

namespace AetherAprs.Modems.Afsk;

/// <summary>
/// RF channel-access timing for the AFSK modem: a randomized p-persistent
/// slottime backoff before keying the transmitter, and a holdoff table that
/// suppresses re-transmission of a frame recently sent over the air.
/// </summary>
/// <remarks>
/// KISS TNCs implement channel access (slottime/persist/TX delay/TX tail) in
/// firmware and therefore do not need this helper; it applies only to the
/// software AFSK modem, which has no hardware carrier-sense or TNC firmware.
/// </remarks>
public class TxTiming
{
    /// <summary>Maximum number of slottimes the backoff may defer transmission.</summary>
    private const int MaxChannelAccessSlots = 32;

    /// <summary>How long a transmitted frame stays in the holdoff table.</summary>
    private static readonly TimeSpan HoldoffRetention = TimeSpan.FromSeconds(30);

    /// <summary>Maximum number of frames tracked for transmit holdoff.</summary>
    private const int HoldoffCapacity = 256;

    private readonly Random _random;
    private readonly object _holdoffLock = new();
    private readonly Queue<(string Key, DateTimeOffset Seen)> _holdoffOrder = new();
    private readonly Dictionary<string, DateTimeOffset> _holdoffTable = new();

    /// <summary>
    /// Initializes a new instance of <see cref="TxTiming"/>.
    /// </summary>
    /// <param name="random">
    /// Source of randomness for the channel-access backoff. Defaults to
    /// <see cref="Random.Shared"/>.
    /// </param>
    public TxTiming(Random? random = null)
    {
        _random = random ?? Random.Shared;
    }

    /// <summary>
    /// Computes the p-persistent channel-access delay. In each slottime the modem
    /// transmits with probability <paramref name="persist"/>/256, otherwise it defers
    /// one slottime and tries again, bounded to <see cref="MaxChannelAccessSlots"/>
    /// slots.
    /// </summary>
    /// <param name="slotTimeMs">The slottime in milliseconds. Zero disables the backoff.</param>
    /// <param name="persist">P-persistence in the range 0-255. Higher transmits sooner.</param>
    /// <returns>The backoff to wait before keying the transmitter.</returns>
    public TimeSpan ComputeChannelAccessDelay(int slotTimeMs, int persist)
    {
        if (slotTimeMs <= 0)
        {
            return TimeSpan.Zero;
        }

        var persistence = Math.Clamp(persist, 1, 255);
        var slots = 0;

        while (slots < MaxChannelAccessSlots && _random.Next(256) >= persistence)
        {
            slots++;
        }

        return TimeSpan.FromMilliseconds(slots * slotTimeMs);
    }

    /// <summary>
    /// Returns <c>true</c> when <paramref name="frameKey"/> was transmitted within the
    /// holdoff window and must not be transmitted again yet.
    /// </summary>
    /// <param name="frameKey">The frame identity (e.g. <c>source|destination|info</c>).</param>
    /// <param name="now">The current time.</param>
    public bool ShouldHoldOff(string frameKey, DateTimeOffset now)
    {
        lock (_holdoffLock)
        {
            return _holdoffTable.TryGetValue(frameKey, out var seen) && now - seen < HoldoffRetention;
        }
    }

    /// <summary>
    /// Records that <paramref name="frameKey"/> was transmitted at <paramref name="now"/>,
    /// evicting expired and excess entries to keep the table bounded.
    /// </summary>
    /// <param name="frameKey">The frame identity (e.g. <c>source|destination|info</c>).</param>
    /// <param name="now">The time the frame was transmitted.</param>
    public void MarkTransmitted(string frameKey, DateTimeOffset now)
    {
        lock (_holdoffLock)
        {
            _holdoffTable[frameKey] = now;
            _holdoffOrder.Enqueue((frameKey, now));

            while (_holdoffOrder.Count > 0)
            {
                var oldest = _holdoffOrder.Peek();
                var expired = now - oldest.Seen >= HoldoffRetention;
                if (!expired && _holdoffOrder.Count <= HoldoffCapacity)
                {
                    break;
                }

                _holdoffOrder.Dequeue();

                // Only remove the table entry when it is still the same transmission;
                // a re-transmitted frame refreshes its timestamp and must not be dropped.
                if (_holdoffTable.TryGetValue(oldest.Key, out var seen) && seen == oldest.Seen)
                {
                    _holdoffTable.Remove(oldest.Key);
                }
            }
        }
    }
}
