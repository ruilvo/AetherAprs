// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;

using AetherAprs.Modems.Kiss;

using Xunit;

namespace AetherAprs.Tests.Modems.Kiss;

public class KissSerializerTests
{
    // ---------------------------------------------------------------
    // EscapeData
    // ---------------------------------------------------------------

    [Fact]
    public void EscapeData_Empty_ReturnsEmpty()
    {
        byte[] result = KissSerializer.EscapeData([]);

        Assert.Empty(result);
    }

    [Fact]
    public void EscapeData_NoSpecialBytes_ReturnsCopy()
    {
        byte[] data = [0x01, 0x02, 0x03, 0xAA, 0xBB];

        byte[] result = KissSerializer.EscapeData(data);

        Assert.Equal(data, result);
    }

    [Fact]
    public void EscapeData_FENDByte_IsEscaped()
    {
        byte[] data = [0xC0];

        byte[] result = KissSerializer.EscapeData(data);

        Assert.Equal([0xDB, 0xDC], result);
    }

    [Fact]
    public void EscapeData_FESCByte_IsEscaped()
    {
        byte[] data = [0xDB];

        byte[] result = KissSerializer.EscapeData(data);

        Assert.Equal([0xDB, 0xDD], result);
    }

    [Fact]
    public void EscapeData_MultipleFEND_AllEscaped()
    {
        byte[] data = [0xC0, 0xC0, 0xC0];

        byte[] result = KissSerializer.EscapeData(data);

        Assert.Equal([0xDB, 0xDC, 0xDB, 0xDC, 0xDB, 0xDC], result);
    }

    [Fact]
    public void EscapeData_MultipleFESC_AllEscaped()
    {
        byte[] data = [0xDB, 0xDB];

        byte[] result = KissSerializer.EscapeData(data);

        Assert.Equal([0xDB, 0xDD, 0xDB, 0xDD], result);
    }

    [Fact]
    public void EscapeData_MixedFENDAndFESC_AllEscaped()
    {
        byte[] data = [0xC0, 0x01, 0xDB, 0x02, 0xC0];

        byte[] result = KissSerializer.EscapeData(data);

        Assert.Equal([0xDB, 0xDC, 0x01, 0xDB, 0xDD, 0x02, 0xDB, 0xDC], result);
    }

    [Fact]
    public void EscapeData_RegularBytes_Unchanged()
    {
        byte[] data = [0x00, 0x01, 0x7F, 0x80, 0xFE, 0xFF];

        byte[] result = KissSerializer.EscapeData(data);

        Assert.Equal(data, result);
    }

    // ---------------------------------------------------------------
    // UnescapeData
    // ---------------------------------------------------------------

    [Fact]
    public void UnescapeData_Empty_ReturnsEmpty()
    {
        byte[] result = KissSerializer.UnescapeData([]);

        Assert.Empty(result);
    }

    [Fact]
    public void UnescapeData_NoEscapeSequences_ReturnsCopy()
    {
        byte[] data = [0x01, 0x02, 0x03, 0xAA];

        byte[] result = KissSerializer.UnescapeData(data);

        Assert.Equal(data, result);
    }

    [Fact]
    public void UnescapeData_FESC_TFEND_Becomes_FEND()
    {
        byte[] data = [0xDB, 0xDC];

        byte[] result = KissSerializer.UnescapeData(data);

        Assert.Equal([0xC0], result);
    }

    [Fact]
    public void UnescapeData_FESC_TFESC_Becomes_FESC()
    {
        byte[] data = [0xDB, 0xDD];

        byte[] result = KissSerializer.UnescapeData(data);

        Assert.Equal([0xDB], result);
    }

    [Fact]
    public void UnescapeData_MultipleEscapes_AllRestored()
    {
        byte[] data = [0xDB, 0xDC, 0xDB, 0xDD, 0xDB, 0xDC];

        byte[] result = KissSerializer.UnescapeData(data);

        Assert.Equal([0xC0, 0xDB, 0xC0], result);
    }

    [Fact]
    public void UnescapeData_LoneFESCWithoutFollow_ThrowsArgumentException()
    {
        byte[] data = [0xDB];

        ArgumentException exception = Assert.Throws<ArgumentException>(() => KissSerializer.UnescapeData(data));

        Assert.Contains("Incomplete escape sequence", exception.Message);
    }

    [Fact]
    public void UnescapeData_InvalidEscapeSequence_ThrowsArgumentException()
    {
        byte[] data = [0xDB, 0x42];

        ArgumentException exception = Assert.Throws<ArgumentException>(() => KissSerializer.UnescapeData(data));

        Assert.Contains("Invalid escape sequence", exception.Message);
        Assert.Contains("0x42", exception.Message);
    }

    // ---------------------------------------------------------------
    // EscapeData / UnescapeData round-trips
    // ---------------------------------------------------------------

    [Fact]
    public void EscapeThenUnescape_RoundTrips_OriginalData()
    {
        byte[] original = [0xC0, 0x01, 0xDB, 0x02, 0xC0, 0x03, 0xDB];

        byte[] escaped = KissSerializer.EscapeData(original);
        byte[] unescaped = KissSerializer.UnescapeData(escaped);

        Assert.Equal(original, unescaped);
    }

    [Fact]
    public void EscapeThenUnescape_Empty_RoundTrips()
    {
        byte[] escaped = KissSerializer.EscapeData([]);
        byte[] unescaped = KissSerializer.UnescapeData(escaped);

        Assert.Empty(unescaped);
    }

    [Fact]
    public void EscapeThenUnescape_NoSpecialBytes_RoundTrips()
    {
        byte[] original = [0x01, 0x02, 0x03, 0xFF];

        byte[] escaped = KissSerializer.EscapeData(original);
        byte[] unescaped = KissSerializer.UnescapeData(escaped);

        Assert.Equal(original, unescaped);
    }

    // ---------------------------------------------------------------
    // EncodeFrame
    // ---------------------------------------------------------------

    [Fact]
    public void EncodeFrame_DataFrameWithEmptyPayload_ProducesMinimalFrame()
    {
        KissFrame frame = new(0x00, []);

        byte[] result = KissSerializer.EncodeFrame(frame);

        // FEND + command + FEND
        Assert.Equal(3, result.Length);
        Assert.Equal(0xC0, result[0]);  // opening FEND
        Assert.Equal(0x00, result[1]);  // command byte
        Assert.Equal(0xC0, result[^1]); // closing FEND
    }

    [Fact]
    public void EncodeFrame_DataFrameWithPayload_IncludesCommandAndEscapedData()
    {
        KissFrame frame = new(0x00, [0x01, 0x02, 0x03]);

        byte[] result = KissSerializer.EncodeFrame(frame);

        // FEND + cmd + 3 data + FEND = 6
        Assert.Equal(6, result.Length);
        Assert.Equal(0xC0, result[0]);
        Assert.Equal(0x00, result[1]);
        Assert.Equal(0x01, result[2]);
        Assert.Equal(0x02, result[3]);
        Assert.Equal(0x03, result[4]);
        Assert.Equal(0xC0, result[5]);
    }

    [Fact]
    public void EncodeFrame_PayloadWithFEND_IsEscapedInFrame()
    {
        KissFrame frame = new(0x00, [0xC0]);

        byte[] result = KissSerializer.EncodeFrame(frame);

        // FEND + cmd + FESC(0xDB) + TFEND(0xDC) + FEND = 5
        Assert.Equal(5, result.Length);
        Assert.Equal(0xC0, result[0]); // opening FEND
        Assert.Equal(0x00, result[1]); // command
        Assert.Equal(0xDB, result[2]); // FESC
        Assert.Equal(0xDC, result[3]); // TFEND
        Assert.Equal(0xC0, result[4]); // closing FEND
    }

    [Fact]
    public void EncodeFrame_PayloadWithFESC_IsEscapedInFrame()
    {
        KissFrame frame = new(0x00, [0xDB]);

        byte[] result = KissSerializer.EncodeFrame(frame);

        // FEND + cmd + FESC(0xDB) + TFESC(0xDD) + FEND = 5
        Assert.Equal(5, result.Length);
        Assert.Equal(0xC0, result[0]); // opening FEND
        Assert.Equal(0x00, result[1]); // command
        Assert.Equal(0xDB, result[2]); // FESC
        Assert.Equal(0xDD, result[3]); // TFESC
        Assert.Equal(0xC0, result[4]); // closing FEND
    }

    [Fact]
    public void EncodeFrame_CommandByte_IsPreserved()
    {
        KissFrame frame = new(0x42, [0x01]);

        byte[] result = KissSerializer.EncodeFrame(frame);

        Assert.Equal(0x42, result[1]);
    }

    [Fact]
    public void EncodeFrame_TXDelayCommand_ProducesCorrectFrame()
    {
        KissFrame frame = new((byte)KissCommandType.TXDelay, [0x05]);

        byte[] result = KissSerializer.EncodeFrame(frame);

        Assert.Equal(0xC0, result[0]);
        Assert.Equal(0x01, result[1]); // TXDelay = 0x01
        Assert.Equal(0x05, result[2]);
        Assert.Equal(0xC0, result[^1]);
    }

    [Fact]
    public void EncodeFrame_PortNumberInUpperBits_IsPreserved()
    {
        // Port 3 (0x30) | DataFrame (0x00) = 0x30
        KissFrame frame = new(0x30, [0x01]);

        byte[] result = KissSerializer.EncodeFrame(frame);

        Assert.Equal(0x30, result[1]);
    }

    // ---------------------------------------------------------------
    // DecodeFrame
    // ---------------------------------------------------------------

    [Fact]
    public void DecodeFrame_EmptyData_ThrowsArgumentException()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(() => KissSerializer.DecodeFrame([]));

        Assert.Contains("empty", exception.Message.ToLowerInvariant());
    }

    [Fact]
    public void DecodeFrame_SingleByte_ReturnsFrameWithEmptyData()
    {
        KissFrame frame = KissSerializer.DecodeFrame([0x00]);

        Assert.Equal(0x00, frame.Command);
        Assert.Empty(frame.Data);
    }

    [Fact]
    public void DecodeFrame_CommandByte_IsFirstByte()
    {
        KissFrame frame = KissSerializer.DecodeFrame([0x42, 0x01, 0x02]);

        Assert.Equal(0x42, frame.Command);
    }

    [Fact]
    public void DecodeFrame_DataBytes_ArePreserved()
    {
        KissFrame frame = KissSerializer.DecodeFrame([0x00, 0x01, 0x02, 0x03]);

        Assert.Equal([0x01, 0x02, 0x03], frame.Data);
    }

    [Fact]
    public void DecodeFrame_ReturnCommand_SetsCommandType()
    {
        // Return (0xFF) & 0x0F = 0x0F = KissCommandType.Return
        KissFrame frame = KissSerializer.DecodeFrame([0xFF]);

        Assert.Equal(KissCommandType.Return, frame.CommandType);
    }

    [Fact]
    public void DecodeFrame_DataFrameWithUpperBits_SetsPort()
    {
        // Port 7 (0x70) | DataFrame (0x00) = 0x70
        KissFrame frame = KissSerializer.DecodeFrame([0x70]);

        Assert.Equal(KissCommandType.DataFrame, frame.CommandType);
        Assert.Equal(7, frame.Port);
    }
}
