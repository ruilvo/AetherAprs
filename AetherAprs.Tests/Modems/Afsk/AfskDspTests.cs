// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Text;

using AetherAprs.Modems.Afsk;

using Xunit;

namespace AetherAprs.Tests.Modems.Afsk;

public class AfskDspTests
{
    // 48000 Hz gives an integer 40 samples per bit (48000 / 1200), so the
    // modulator and demodulator bit grids align exactly.
    private const int SampleRate = 48000;

    [Fact]
    public void Modulate_ThenDemodulate_RecoversFrame()
    {
        byte[] data = Encoding.ASCII.GetBytes("APRS TEST 123");
        byte[] bits = HdlcFramer.Encode(data);
        float[] samples = AfskModulator.Modulate(bits, SampleRate);

        AfskDemodulator demodulator = new(SampleRate);
        List<byte[]> frames = [.. demodulator.Process(samples), .. demodulator.Flush()];

        Assert.Contains(frames, f => f.AsSpan().SequenceEqual(data));
    }

    [Fact]
    public void Modulate_ThenDemodulate_StuffedFrameRoundTrips()
    {
        byte[] data = [0xFF, 0x00, 0x7E, 0x55, 0xAA];
        byte[] bits = HdlcFramer.Encode(data);
        float[] samples = AfskModulator.Modulate(bits, SampleRate);

        AfskDemodulator demodulator = new(SampleRate);
        List<byte[]> frames = [.. demodulator.Process(samples), .. demodulator.Flush()];

        Assert.Contains(frames, f => f.AsSpan().SequenceEqual(data));
    }

    [Fact]
    public void Modulate_EmptyBitStream_ReturnsEmptySamples() => Assert.Empty(AfskModulator.Modulate([], SampleRate));

    [Fact]
    public void Modulate_InvalidSampleRate_Throws()
    {
        byte[] bits = HdlcFramer.Encode([0x01]);
        Assert.Throws<ArgumentOutOfRangeException>(() => AfskModulator.Modulate(bits, 0));
    }

    [Fact]
    public void Modulate_InvalidAmplitude_Throws()
    {
        byte[] bits = HdlcFramer.Encode([0x01]);
        Assert.Throws<ArgumentOutOfRangeException>(() => AfskModulator.Modulate(bits, SampleRate, 0.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => AfskModulator.Modulate(bits, SampleRate, 1.5));
    }

    [Fact]
    public void Demodulator_InvalidSampleRate_Throws() => Assert.Throws<ArgumentOutOfRangeException>(() => new AfskDemodulator(0));

    [Fact]
    public void Demodulator_Reset_ClearsState()
    {
        byte[] data = Encoding.ASCII.GetBytes("HELLO");
        byte[] bits = HdlcFramer.Encode(data);
        float[] samples = AfskModulator.Modulate(bits, SampleRate);

        AfskDemodulator demodulator = new(SampleRate);
        demodulator.Process(samples);
        demodulator.Reset();

        // After reset, a fresh demodulation still recovers the frame.
        List<byte[]> frames = [.. demodulator.Process(samples), .. demodulator.Flush()];

        Assert.Contains(frames, f => f.AsSpan().SequenceEqual(data));
    }
}
