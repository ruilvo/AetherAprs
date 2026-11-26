// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using AetherAprs.Modems.Afsk;
using Xunit;

namespace AetherAprs.Tests.Modems.Afsk;

public sealed class TxTimingTests
{
    // ---------------------------------------------------------------
    // Channel access (p-persistent slottime backoff)
    // ---------------------------------------------------------------

    [Fact]
    public void ComputeChannelAccessDelay_ZeroSlotTime_ReturnsZero()
    {
        TxTiming timing = new(new FixedRandom(255));

        Assert.Equal(TimeSpan.Zero, timing.ComputeChannelAccessDelay(0, 63));
    }

    [Fact]
    public void ComputeChannelAccessDelay_MaxPersist_TransmitsImmediately()
    {
        // persist=255 clamps to 255; a draw of 254 is below it, so zero slots.
        TxTiming timing = new(new FixedRandom(254));

        Assert.Equal(TimeSpan.Zero, timing.ComputeChannelAccessDelay(100, 255));
    }

    [Fact]
    public void ComputeChannelAccessDelay_LowPersist_DefersToBoundedSlots()
    {
        // persist=1 and a draw of 255 always defers, bounded to 32 slots.
        TxTiming timing = new(new FixedRandom(255));

        Assert.Equal(TimeSpan.FromMilliseconds(3200), timing.ComputeChannelAccessDelay(100, 1));
    }

    [Fact]
    public void ComputeChannelAccessDelay_IsMultipleOfSlotTime()
    {
        TxTiming timing = new(new Random(12345));

        TimeSpan delay = timing.ComputeChannelAccessDelay(50, 63);

        Assert.Equal(0, delay.TotalMilliseconds % 50);
        Assert.InRange(delay.TotalMilliseconds, 0, 50 * 32);
    }

    // ---------------------------------------------------------------
    // Transmit holdoff
    // ---------------------------------------------------------------

    [Fact]
    public void ShouldHoldOff_UnseenKey_ReturnsFalse()
    {
        TxTiming timing = new();

        Assert.False(timing.ShouldHoldOff("N0CALL|APRS|data", DateTimeOffset.UtcNow));
    }

    [Fact]
    public void ShouldHoldOff_AfterMarkTransmitted_WithinWindow_ReturnsTrue()
    {
        TxTiming timing = new();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        timing.MarkTransmitted("N0CALL|APRS|data", now);

        Assert.True(timing.ShouldHoldOff("N0CALL|APRS|data", now + TimeSpan.FromSeconds(29)));
    }

    [Fact]
    public void ShouldHoldOff_AfterWindow_ReturnsFalse()
    {
        TxTiming timing = new();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        timing.MarkTransmitted("N0CALL|APRS|data", now);

        Assert.False(timing.ShouldHoldOff("N0CALL|APRS|data", now + TimeSpan.FromSeconds(31)));
    }

    [Fact]
    public void MarkTransmitted_BeyondCapacity_EvictsOldest()
    {
        TxTiming timing = new();
        DateTimeOffset now = DateTimeOffset.UtcNow;

        for (int i = 0; i <= 256; i++)
        {
            timing.MarkTransmitted($"key{i}", now);
        }

        Assert.False(timing.ShouldHoldOff("key0", now));    // evicted (oldest)
        Assert.True(timing.ShouldHoldOff("key256", now));   // still present
    }
}

/// <summary>Deterministic <see cref="Random"/> for timing tests.</summary>
internal sealed class FixedRandom(int value) : Random
{
    public override int Next(int maxValue) => value;
}
