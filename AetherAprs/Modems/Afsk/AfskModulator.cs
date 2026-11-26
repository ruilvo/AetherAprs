// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;

namespace AetherAprs.Modems.Afsk;

/// <summary>
/// Bell 202 AFSK modulator: converts a serialized bit stream into a
/// continuous-phase PCM float sample stream.
///
/// Bits are NRZI-encoded (a <c>0</c> toggles the tone, a <c>1</c> holds it)
/// and then mapped to two audio tones: mark = 1200 Hz, space = 2200 Hz, at a
/// 1200 baud symbol rate. A shared phase accumulator keeps the waveform
/// continuous across tone changes, avoiding clicks.
/// </summary>
public static class AfskModulator
{
    /// <summary>Mark tone frequency in hertz (a logical "1" after NRZI).</summary>
    public const double MarkFrequencyHz = 1200.0;

    /// <summary>Space tone frequency in hertz (a logical "0" after NRZI).</summary>
    public const double SpaceFrequencyHz = 2200.0;

    /// <summary>Symbol (baud) rate in bits per second.</summary>
    public const int BaudRate = 1200;

    /// <summary>
    /// Modulates a bit stream into PCM float samples.
    /// </summary>
    /// <param name="bits">The serialized bit stream (each element 0 or 1).</param>
    /// <param name="sampleRate">The output sample rate in hertz.</param>
    /// <param name="amplitude">Peak amplitude in the range (0, 1].</param>
    /// <returns>Mono PCM float samples in the range [-amplitude, amplitude].</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown if <paramref name="sampleRate"/> is not positive or
    /// <paramref name="amplitude"/> is outside (0, 1].
    /// </exception>
    public static float[] Modulate(ReadOnlySpan<byte> bits, int sampleRate, double amplitude = 0.9)
    {
        if (sampleRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRate), sampleRate, "Sample rate must be positive.");
        }

        if (amplitude is <= 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(amplitude), amplitude, "Amplitude must be in (0, 1].");
        }

        if (bits.IsEmpty)
        {
            return [];
        }

        double samplesPerBit = (double)sampleRate / BaudRate;
        int totalSamples = (int)Math.Ceiling(bits.Length * samplesPerBit);
        float[] samples = new float[totalSamples];

        // Precompute the tone for each bit via NRZI: 0 toggles, 1 holds.
        bool[] tones = new bool[bits.Length];
        bool mark = true;
        for (int i = 0; i < bits.Length; i++)
        {
            if (bits[i] == 0)
            {
                mark = !mark;
            }

            tones[i] = mark;
        }

        double phase = 0.0;
        double markStep = 2.0 * Math.PI * MarkFrequencyHz / sampleRate;
        double spaceStep = 2.0 * Math.PI * SpaceFrequencyHz / sampleRate;

        for (int i = 0; i < totalSamples; i++)
        {
            int bitIndex = (int)(i / samplesPerBit);
            if (bitIndex >= bits.Length)
            {
                bitIndex = bits.Length - 1;
            }

            phase += tones[bitIndex] ? markStep : spaceStep;
            samples[i] = (float)(amplitude * Math.Sin(phase));
        }

        return samples;
    }
}
