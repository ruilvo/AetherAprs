// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Text;
using AetherAprs.Modems.Afsk;
using Xunit;

namespace AetherAprs.Tests.Modems.Afsk;

public class HdlcFramerTests
{
    // ---------------------------------------------------------------
    // CRC-16/X.25 FCS
    // ---------------------------------------------------------------

    [Fact]
    public void ComputeFcs_Ascii123456789_ReturnsKnownCheckValue()
    {
        // CRC-16/X.25 check value for the standard "123456789" test vector.
        byte[] data = Encoding.ASCII.GetBytes("123456789");

        Assert.Equal((ushort)0x906E, HdlcFramer.ComputeFcs(data));
    }

    [Fact]
    public void ComputeFcs_EmptyData_ReturnsZeroXorOut() =>
        // Raw CRC of empty input is the initial 0xFFFF, XORed out back to 0x0000.
        Assert.Equal((ushort)0x0000, HdlcFramer.ComputeFcs([]));

    [Fact]
    public void IsFcsValid_ValidFrameWithFcs_ReturnsTrue()
    {
        byte[] data = [0x01, 0x02, 0x03, 0x04];
        ushort fcs = HdlcFramer.ComputeFcs(data);
        byte[] frame = [.. data, (byte)(fcs & 0xFF), (byte)(fcs >> 8)];

        Assert.True(HdlcFramer.IsFcsValid(frame));
    }

    [Fact]
    public void IsFcsValid_CorruptedFrame_ReturnsFalse()
    {
        byte[] data = [0x01, 0x02, 0x03, 0x04];
        ushort fcs = HdlcFramer.ComputeFcs(data);
        byte[] frame = [.. data, (byte)(fcs & 0xFF), (byte)(fcs >> 8)];
        frame[0] ^= 0xFF; // corrupt the first data byte

        Assert.False(HdlcFramer.IsFcsValid(frame));
    }

    [Fact]
    public void IsFcsValid_TooShort_ReturnsFalse() => Assert.False(HdlcFramer.IsFcsValid([0x01]));

    // ---------------------------------------------------------------
    // HDLC encode -> deframe round-trip
    // ---------------------------------------------------------------

    [Fact]
    public void Encode_ThenDeframe_RecoversOriginalData()
    {
        byte[] data = Encoding.ASCII.GetBytes("APRS TEST 123");
        byte[] bits = HdlcFramer.Encode(data);

        HdlcDeframer deframer = new();
        byte[]? result = null;
        foreach (byte bit in bits)
        {
            byte[]? frame = deframer.Feed(bit);
            if (frame is not null)
            {
                result = frame;
            }
        }

        Assert.NotNull(result);
        Assert.Equal(data, result);
    }

    [Fact]
    public void Encode_DataWithConsecutiveOnes_IsBitStuffedAndRoundTrips()
    {
        // 0xFF is eight consecutive ones, forcing bit-stuffing in both the data
        // and the FCS bytes.
        byte[] data = [0xFF, 0x00, 0xFF, 0x7E, 0x55];
        byte[] bits = HdlcFramer.Encode(data);

        HdlcDeframer deframer = new();
        byte[]? result = null;
        foreach (byte bit in bits)
        {
            byte[]? frame = deframer.Feed(bit);
            if (frame is not null)
            {
                result = frame;
            }
        }

        Assert.NotNull(result);
        Assert.Equal(data, result);
    }

    [Fact]
    public void Encode_BeginsAndEndsWithFlag()
    {
        byte[] bits = HdlcFramer.Encode([0x01, 0x02]);

        Assert.Equal(0, bits[0]);          // leading flag first bit
        Assert.Equal(1, bits[1]);
        Assert.Equal(0, bits[^1]);         // trailing flag last bit
        Assert.Equal(1, bits[^2]);
    }

    [Fact]
    public void Deframe_TwoFramesSeparatedByFlags_RecoversBoth()
    {
        byte[] frameA = [0x10, 0x20];
        byte[] frameB = [0x30, 0x40, 0x50];
        byte[] bits = [.. HdlcFramer.Encode(frameA), .. HdlcFramer.Encode(frameB)];

        HdlcDeframer deframer = new();
        List<byte[]> frames = [];
        foreach (byte bit in bits)
        {
            byte[]? frame = deframer.Feed(bit);
            if (frame is not null)
            {
                frames.Add(frame);
            }
        }

        Assert.Equal(2, frames.Count);
        Assert.Equal(frameA, frames[0]);
        Assert.Equal(frameB, frames[1]);
    }

    [Fact]
    public void Deframe_InvalidFcs_DoesNotEmitFrame()
    {
        byte[] data = [0x01, 0x02, 0x03];
        // Build a frame by hand and flip one payload bit to corrupt the FCS.
        ushort fcs = HdlcFramer.ComputeFcs(data);
        byte[] payload = [.. data, (byte)(fcs & 0xFF), (byte)(fcs >> 8)];
        payload[0] ^= 0x01;

        List<byte> bits = [];
        AppendFlag(bits);
        AppendStuffed(bits, payload);
        AppendFlag(bits);

        HdlcDeframer deframer = new();
        byte[]? result = null;
        foreach (byte bit in bits)
        {
            byte[]? frame = deframer.Feed(bit);
            if (frame is not null)
            {
                result = frame;
            }
        }

        Assert.Null(result);
    }

    // ---------------------------------------------------------------
    // Helpers (mirror HdlcFramer internal packing for the corruption test)
    // ---------------------------------------------------------------

    private static void AppendFlag(System.Collections.Generic.List<byte> bits)
    {
        bits.Add(0);
        for (int i = 0; i < 6; i++)
        {
            bits.Add(1);
        }

        bits.Add(0);
    }

    private static void AppendStuffed(System.Collections.Generic.List<byte> bits, ReadOnlySpan<byte> data)
    {
        int ones = 0;
        foreach (byte value in data)
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
