// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;

namespace AetherAprs.Modems.Afsk;

/// <summary>
/// Bell 202 AFSK demodulator: converts a PCM float sample stream back into
/// decoded AX.25 frames.
///
/// The signal is sliced into 1200-baud bit periods on a grid derived from the
/// sample rate. Each bit period is scored for mark (1200 Hz) and space
/// (2200 Hz) energy using a two-bin discrete Fourier transform; the larger bin
/// selects the tone. Consecutive tones are NRZI-decoded (a tone change is a 0,
/// a held tone is a 1) and fed to an <see cref="HdlcDeframer"/> that
/// reassembles HDLC frames and verifies their CRC-16/X.25 FCS.
/// </summary>
/// <remarks>
/// The bit clock is recovered from the known baud rate and sample rate on a
/// fixed grid that mirrors <see cref="AfskModulator"/>'s bit mapping, so a
/// clean synthesized signal (zero drift) round-trips exactly. A PLL for
/// real-world sample-clock drift is a later slice.
///
/// The NRZI reference tone before the first bit is mark (1200 Hz), matching the
/// modulator's initial state, so the leading flag bit decodes correctly.
/// </remarks>
public sealed class AfskDemodulator
{
    private const double TwoPi = 2.0 * Math.PI;

    private readonly int _sampleRate;
    private readonly double _samplesPerBit;

    // Bit-grid state. _sampleIndex is the global sample position; the current
    // bit is floor(sampleIndex / samplesPerBit), matching the modulator exactly.
    private long _sampleIndex;
    private int _bitIndex = -1;
    private int _sampleInBit;

    // Per-bit DFT accumulators (in-phase and quadrature at each tone).
    private double _markCos;
    private double _markSin;
    private double _spaceCos;
    private double _spaceSin;

    // NRZI decode state. The modulator starts on the mark tone and toggles on
    // a 0 bit, so the receiver's reference before the first bit is mark and
    // "tone changed" decodes to 0.
    private bool _previousToneMark = true;

    private readonly HdlcDeframer _deframer = new();

    /// <summary>
    /// Initializes a new instance of <see cref="AfskDemodulator"/>.
    /// </summary>
    /// <param name="sampleRate">The input sample rate in hertz.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown if <paramref name="sampleRate"/> is not positive.
    /// </exception>
    public AfskDemodulator(int sampleRate)
    {
        if (sampleRate <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sampleRate), sampleRate, "Sample rate must be positive.");
        }

        _sampleRate = sampleRate;
        _samplesPerBit = (double)sampleRate / AfskModulator.BaudRate;
    }

    /// <summary>
    /// Processes a block of PCM samples, returning any AX.25 frames decoded
    /// (data payload, FCS stripped). Trailing partial bits are buffered for
    /// the next call.
    /// </summary>
    /// <param name="samples">Mono PCM float samples.</param>
    /// <returns>The decoded frames, in order of completion.</returns>
    public IReadOnlyList<byte[]> Process(ReadOnlySpan<float> samples)
    {
        List<byte[]> frames = [];
        foreach (float sample in samples)
        {
            ProcessSample(sample, frames);
        }

        return frames;
    }

    /// <summary>
    /// Finalizes the last in-progress bit and returns any frame it completes.
    /// Call this once at the end of a capture so a frame whose trailing flag is
    /// the final bit is not dropped.
    /// </summary>
    /// <returns>Any frames completed by the final bit.</returns>
    public IReadOnlyList<byte[]> Flush()
    {
        List<byte[]> frames = [];
        if (_bitIndex >= 0 && _sampleInBit > 0)
        {
            FinalizeBit(frames);
            _bitIndex = -1;
            _sampleInBit = 0;
        }

        return frames;
    }

    /// <summary>
    /// Resets the demodulator and its internal deframer to a clean state.
    /// </summary>
    public void Reset()
    {
        _sampleIndex = 0;
        _bitIndex = -1;
        _sampleInBit = 0;
        _markCos = 0;
        _markSin = 0;
        _spaceCos = 0;
        _spaceSin = 0;
        _previousToneMark = true;
        _deframer.Reset();
    }

    private void ProcessSample(float sample, List<byte[]> frames)
    {
        int bitIndex = (int)(_sampleIndex / _samplesPerBit);
        _sampleIndex++;

        if (bitIndex != _bitIndex)
        {
            if (_bitIndex >= 0)
            {
                FinalizeBit(frames);
            }

            _bitIndex = bitIndex;
            _sampleInBit = 0;
        }

        double markAngle = TwoPi * AfskModulator.MarkFrequencyHz * _sampleInBit / _sampleRate;
        double spaceAngle = TwoPi * AfskModulator.SpaceFrequencyHz * _sampleInBit / _sampleRate;

        _markCos += sample * Math.Cos(markAngle);
        _markSin += sample * Math.Sin(markAngle);
        _spaceCos += sample * Math.Cos(spaceAngle);
        _spaceSin += sample * Math.Sin(spaceAngle);

        _sampleInBit++;
    }

    private void FinalizeBit(List<byte[]> frames)
    {
        double markMagnitudeSquared = (_markCos * _markCos) + (_markSin * _markSin);
        double spaceMagnitudeSquared = (_spaceCos * _spaceCos) + (_spaceSin * _spaceSin);
        bool toneIsMark = markMagnitudeSquared >= spaceMagnitudeSquared;

        // NRZI: a tone change is a 0 bit, a held tone is a 1 bit.
        byte dataBit = toneIsMark == _previousToneMark ? (byte)1 : (byte)0;
        byte[]? frame = _deframer.Feed(dataBit);
        if (frame is not null)
        {
            frames.Add(frame);
        }

        _previousToneMark = toneIsMark;

        _markCos = 0;
        _markSin = 0;
        _spaceCos = 0;
        _spaceSin = 0;
    }
}
