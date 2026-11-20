// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;

namespace AetherAprs.Modems.Afsk;

/// <summary>
/// HDLC framing for AX.25 over AFSK: flag bytes (0x7E), bit-stuffing, and the
/// CRC-16/X.25 frame check sequence. Produces and consumes a serialized bit
/// stream (each element 0 or 1) with each byte transmitted least-significant
/// bit first.
/// </summary>
public static class HdlcFramer
{
    /// <summary>HDLC flag byte.</summary>
    public const byte Flag = 0x7E;

    /// <summary>CRC-16/X.25 generator polynomial in reflected form.</summary>
    private const ushort Polynomial = 0x8408;

    /// <summary>CRC initial value.</summary>
    private const ushort InitialValue = 0xFFFF;

    /// <summary>Residue of a valid frame (data + FCS) when the raw CRC is recomputed.</summary>
    internal const ushort ValidResidue = 0xF0B8;

    /// <summary>
    /// Computes the CRC-16/X.25 frame check sequence for a data sequence.
    /// The result is transmitted least-significant byte first.
    /// </summary>
    /// <param name="data">The data bytes to protect.</param>
    /// <returns>The 16-bit FCS.</returns>
    public static ushort ComputeFcs(ReadOnlySpan<byte> data)
    {
        return (ushort)(ComputeRawCrc(data) ^ 0xFFFF);
    }

    /// <summary>
    /// Encodes data into an HDLC bit stream: a leading flag, the bit-stuffed
    /// payload followed by the bit-stuffed FCS (low byte first), and a trailing
    /// flag. Each returned element is a single bit (0 or 1).
    /// </summary>
    /// <param name="data">The data bytes to frame.</param>
    /// <returns>The serialized bit stream.</returns>
    public static byte[] Encode(ReadOnlySpan<byte> data)
    {
        ushort fcs = ComputeFcs(data);

        var payload = new byte[data.Length + 2];
        data.CopyTo(payload);
        payload[data.Length] = (byte)(fcs & 0xFF);
        payload[data.Length + 1] = (byte)(fcs >> 8);

        var bits = new List<byte>((payload.Length * 9) + 16);
        AppendFlag(bits);
        AppendStuffed(bits, payload);
        AppendFlag(bits);
        return bits.ToArray();
    }

    /// <summary>
    /// Validates a received frame (data plus trailing two FCS bytes) by
    /// recomputing the raw CRC and comparing it to the fixed residue.
    /// </summary>
    internal static bool IsFcsValid(ReadOnlySpan<byte> frameWithFcs)
    {
        return frameWithFcs.Length >= 2 && ComputeRawCrc(frameWithFcs) == ValidResidue;
    }

    private static ushort ComputeRawCrc(ReadOnlySpan<byte> data)
    {
        ushort crc = InitialValue;
        foreach (var value in data)
        {
            crc ^= value;
            for (int i = 0; i < 8; i++)
            {
                crc = (crc & 1) != 0 ? (ushort)((crc >> 1) ^ Polynomial) : (ushort)(crc >> 1);
            }
        }

        return crc;
    }

    private static void AppendFlag(List<byte> bits)
    {
        // 0x7E = 01111110, transmitted least-significant bit first.
        bits.Add(0);
        for (int i = 0; i < 6; i++)
        {
            bits.Add(1);
        }

        bits.Add(0);
    }

    private static void AppendStuffed(List<byte> bits, ReadOnlySpan<byte> data)
    {
        int ones = 0;
        foreach (var value in data)
        {
            for (int i = 0; i < 8; i++)
            {
                byte bit = (byte)((value >> i) & 1);
                bits.Add(bit);

                if (bit == 1)
                {
                    ones++;
                    if (ones == 5)
                    {
                        // Stuff a zero after five consecutive ones.
                        bits.Add(0);
                        ones = 0;
                    }
                }
                else
                {
                    ones = 0;
                }
            }
        }
    }
}

/// <summary>
/// Stateful HDLC deframer that reconstructs complete frames from a serialized
/// bit stream. Detects 0x7E flags, removes bit-stuffing, verifies the
/// CRC-16/X.25 FCS, and returns the data payload of valid frames.
/// </summary>
public sealed class HdlcDeframer
{
    /// <summary>
    /// Maximum accumulated frame size in bits before the input is treated as
    /// malformed. A maximum AX.25 UI frame is ~330 bytes; anything far above
    /// this means no valid flag will ever arrive.
    /// </summary>
    private const int MaxFrameBits = 4096;

    private readonly List<byte> _frame = new(256);
    private bool _inFrame;
    private int _ones;

    /// <summary>
    /// Feeds a single received bit (0 or 1). Returns the data payload (FCS
    /// stripped) when a valid frame completes, otherwise null.
    /// </summary>
    /// <param name="bit">The received bit (0 or 1).</param>
    /// <returns>The frame payload, or null if no frame completed.</returns>
    public byte[]? Feed(byte bit)
    {
        // A one advances the consecutive-ones counter but is only committed to
        // the frame once its context is known (stuffed data vs. flag).
        if (bit == 1)
        {
            _ones++;

            if (_ones == 6)
            {
                // Six consecutive ones is the "111111" of a flag; the trailing
                // zero completes it. Do not commit these six ones.
                return null;
            }

            if (_ones == 7)
            {
                // Seven ones is an abort/idle condition: discard the current
                // frame and resynchronize on the next flag.
                ResetFrame();
                return null;
            }

            // Not yet in a frame: ignore (still searching for a flag).
            return null;
        }

        // bit == 0
        if (_ones == 5)
        {
            // Stuffed zero: commit the five ones as data, discard the zero.
            if (_inFrame)
            {
                AppendOnes(5);
            }

            _ones = 0;
            return null;
        }

        if (_ones == 6)
        {
            // Trailing zero of a flag: 0 111111 0.
            _ones = 0;

            if (_inFrame)
            {
                return CompleteFrame();
            }

            // Enter a new frame.
            _inFrame = true;
            return null;
        }

        // A zero preceded by fewer than five ones is a plain data bit.
        if (_inFrame)
        {
            AppendOnes(_ones);
            AppendBit(0);
        }

        _ones = 0;
        return null;
    }

    /// <summary>
    /// Resets the deframer to the searching-for-flag state.
    /// </summary>
    public void Reset()
    {
        ResetFrame();
    }

    private byte[]? CompleteFrame()
    {
        // The flag's leading zero was committed as a data bit; drop it. If the
        // remainder is empty (two consecutive flags) there is no frame to emit.
        if (_frame.Count > 0)
        {
            _frame.RemoveAt(_frame.Count - 1);
        }

        var payload = TryExtractPayload();
        _inFrame = false;
        _frame.Clear();
        _ones = 0;
        return payload;
    }

    private byte[]? TryExtractPayload()
    {
        // A frame shorter than one data byte plus the two FCS bytes is not a
        // meaningful frame.
        if (_frame.Count < 24)
        {
            return null;
        }

        // Bit count must be a whole number of bytes.
        if ((_frame.Count & 7) != 0)
        {
            return null;
        }

        var bytes = new byte[_frame.Count / 8];
        for (int i = 0; i < bytes.Length; i++)
        {
            byte value = 0;
            for (int j = 0; j < 8; j++)
            {
                if (_frame[(i * 8) + j] == 1)
                {
                    value |= (byte)(1 << j);
                }
            }

            bytes[i] = value;
        }

        if (!HdlcFramer.IsFcsValid(bytes))
        {
            return null;
        }

        // Strip the two trailing FCS bytes.
        return bytes[..^2];
    }

    private void AppendOnes(int count)
    {
        for (int i = 0; i < count; i++)
        {
            AppendBit(1);
        }
    }

    private void AppendBit(byte bit)
    {
        if (_frame.Count >= MaxFrameBits)
        {
            // Unbounded growth means no valid flag will ever arrive; discard.
            ResetFrame();
            return;
        }

        _frame.Add(bit);
    }

    private void ResetFrame()
    {
        _frame.Clear();
        _ones = 0;
        _inFrame = false;
    }
}
