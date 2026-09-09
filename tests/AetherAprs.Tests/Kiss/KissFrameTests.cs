// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Modems.Kiss;
using Xunit;

namespace AetherAprs.Tests.Kiss;

public class KissFrameTests
{
    // ---------------------------------------------------------------
    // Construction
    // ---------------------------------------------------------------

    [Fact]
    public void Constructor_CommandAndData_PropertiesAreSet()
    {
        var frame = new KissFrame(0x12, [0x01, 0x02]);

        Assert.Equal(0x12, frame.Command);
        Assert.Equal([0x01, 0x02], frame.Data);
    }

    [Fact]
    public void Constructor_EmptyData_SetsEmptyArray()
    {
        var frame = new KissFrame(0x00, []);

        Assert.Empty(frame.Data);
    }

    // ---------------------------------------------------------------
    // CommandType
    // ---------------------------------------------------------------

    [Fact]
    public void CommandType_DataFrame_ReturnsDataFrame()
    {
        var frame = new KissFrame(0x00, []);

        Assert.Equal(KissCommandType.DataFrame, frame.CommandType);
    }

    [Fact]
    public void CommandType_TXDelay_ReturnsTXDelay()
    {
        var frame = new KissFrame(0x01, []);

        Assert.Equal(KissCommandType.TXDelay, frame.CommandType);
    }

    [Fact]
    public void CommandType_Persistence_ReturnsPersistence()
    {
        var frame = new KissFrame(0x02, []);

        Assert.Equal(KissCommandType.Persistence, frame.CommandType);
    }

    [Fact]
    public void CommandType_SlotTime_ReturnsSlotTime()
    {
        var frame = new KissFrame(0x03, []);

        Assert.Equal(KissCommandType.SlotTime, frame.CommandType);
    }

    [Fact]
    public void CommandType_TXTail_ReturnsTXTail()
    {
        var frame = new KissFrame(0x04, []);

        Assert.Equal(KissCommandType.TXTail, frame.CommandType);
    }

    [Fact]
    public void CommandType_FullDuplex_ReturnsFullDuplex()
    {
        var frame = new KissFrame(0x05, []);

        Assert.Equal(KissCommandType.FullDuplex, frame.CommandType);
    }

    [Fact]
    public void CommandType_SetHardware_ReturnsSetHardware()
    {
        var frame = new KissFrame(0x06, []);

        Assert.Equal(KissCommandType.SetHardware, frame.CommandType);
    }

    [Fact]
    public void CommandType_Return_ReturnsMappedValue()
    {
        // Return (0xFF) uses all 8 bits. The CommandType property
        // extracts the lower 4 bits: 0xFF & 0x0F = 0x0F = 15.
        var frame = new KissFrame(0xFF, []);

        Assert.Equal((KissCommandType)15, frame.CommandType);
    }

    // ---------------------------------------------------------------
    // CommandType - lower 4 bits extraction
    // ---------------------------------------------------------------

    [Fact]
    public void CommandType_LowerBitsOnly_IgnoresUpperNibble()
    {
        // Upper nibble = 0xA, lower nibble = TXDelay (0x01)
        var frame = new KissFrame(0xA1, []);

        Assert.Equal(KissCommandType.TXDelay, frame.CommandType);
    }

    [Fact]
    public void CommandType_HighUpperNibble_IgnoresUpperNibble()
    {
        // Upper nibble = 0xF, lower nibble = DataFrame (0x00)
        var frame = new KissFrame(0xF0, []);

        Assert.Equal(KissCommandType.DataFrame, frame.CommandType);
    }

    // ---------------------------------------------------------------
    // Port
    // ---------------------------------------------------------------

    [Fact]
    public void Port_DefaultPort0_Returns0()
    {
        var frame = new KissFrame(0x00, []);

        Assert.Equal(0, frame.Port);
    }

    [Fact]
    public void Port_UpperNibbleSet_ReturnsPortNumber()
    {
        var frame = new KissFrame(0x10, []);

        Assert.Equal(1, frame.Port);
    }

    [Fact]
    public void Port_MaxPort_Returns15()
    {
        var frame = new KissFrame(0xF0, []);

        Assert.Equal(15, frame.Port);
    }

    [Fact]
    public void Port_OnlyExtractsUpperNibble_IgnoresLowerNibble()
    {
        // Port 3 (0x30) | TXDelay (0x01) = 0x31
        var frame = new KissFrame(0x31, []);

        Assert.Equal(3, frame.Port);
    }

    [Fact]
    public void Port_CommandByteWithFullMask_ExtractsPortCorrectly()
    {
        // Port 7 (0x70) | Return (0x0F) -- not a standard value but tests the mask
        var frame = new KissFrame(0x7F, []);

        Assert.Equal(7, frame.Port);
    }

    // ---------------------------------------------------------------
    // Combined CommandType + Port
    // ---------------------------------------------------------------

    [Fact]
    public void Frame_WithPortAndCommand_ExtractsBoth()
    {
        // Upper nibble = port 3, lower nibble = Persistence (0x02)
        var frame = new KissFrame(0x32, []);

        Assert.Equal(3, frame.Port);
        Assert.Equal(KissCommandType.Persistence, frame.CommandType);
    }

    [Fact]
    public void Frame_WithPort15AndReturn_Combined()
    {
        // Return (0xFF) uses all 8 bits. The lower 4 bits extract 0x0F = 15,
        // and the upper 4 bits extract 0x0F = port 15.
        var frame = new KissFrame(0xFF, []);

        Assert.Equal(15, frame.Port);
        Assert.Equal((KissCommandType)15, frame.CommandType);
    }

    // ---------------------------------------------------------------
    // Value semantics
    // ---------------------------------------------------------------

    [Fact]
    public void Equals_SameCommandAndData_NotEqualDueToReferenceTypeData()
    {
        // KissFrame is a record struct. For reference type fields (byte[]),
        // equality uses reference equality, not structural equality.
        var frame1 = new KissFrame(0x12, [0x01, 0x02]);
        var frame2 = new KissFrame(0x12, [0x01, 0x02]);

        Assert.NotEqual(frame1, frame2);
    }

    [Fact]
    public void Equals_DifferentCommand_AreNotEqual()
    {
        var frame1 = new KissFrame(0x00, [0x01]);
        var frame2 = new KissFrame(0x01, [0x01]);

        Assert.NotEqual(frame1, frame2);
    }

    [Fact]
    public void Equals_DifferentData_AreNotEqual()
    {
        var frame1 = new KissFrame(0x00, [0x01]);
        var frame2 = new KissFrame(0x00, [0x02]);

        Assert.NotEqual(frame1, frame2);
    }

    // ---------------------------------------------------------------
    // Data mutability
    // ---------------------------------------------------------------

    [Fact]
    public void Data_MutableArray_ChangesAffectFrame()
    {
        // record structs with mutable reference types: the Data array
        // is shared, not defensively copied
        var data = new byte[] { 0x01, 0x02 };
        var frame = new KissFrame(0x00, data);

        data[0] = 0xFF;

        Assert.Equal(0xFF, frame.Data[0]);
    }
}