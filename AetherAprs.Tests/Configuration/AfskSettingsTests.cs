// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using AetherAprs.Configuration;
using Xunit;

namespace AetherAprs.Tests.Configuration;

public sealed class AfskSettingsTests
{
    [Fact]
    public void NewInstance_HasDocumentedDefaults()
    {
        AfskSettings settings = new();

        Assert.Equal(PttMode.None, settings.PttMode);
        Assert.Equal(AfskSettings.DefaultTxDelayMs, settings.TxDelayMs);
        Assert.Equal(AfskSettings.DefaultTxTailMs, settings.TxTailMs);
        Assert.Equal(AfskSettings.DefaultSlotTimeMs, settings.SlotTimeMs);
        Assert.Equal(AfskSettings.DefaultPersist, settings.Persist);
        Assert.Equal(AfskSettings.DefaultAudioLevel, settings.AudioLevel);
        Assert.Equal(AfskSettings.DefaultSquelchThreshold, settings.SquelchThreshold);
    }

    [Theory]
    [InlineData(PttMode.None)]
    [InlineData(PttMode.SpeakerMic)]
    public void PttMode_ValidValues_AreAccepted(PttMode mode)
    {
        AfskSettings settings = new() { PttMode = mode };
        Assert.Equal(mode, settings.PttMode);
    }

    [Fact]
    public void PttMode_InvalidValue_Throws()
    {
        AfskSettings settings = new();
        Assert.Throws<ArgumentOutOfRangeException>(() => settings.PttMode = (PttMode)99);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void TxDelayMs_Negative_Throws(int value)
    {
        AfskSettings settings = new();
        Assert.Throws<ArgumentOutOfRangeException>(() => settings.TxDelayMs = value);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(300)]
    public void TxDelayMs_NonNegative_IsAccepted(int value)
    {
        AfskSettings settings = new() { TxDelayMs = value };
        Assert.Equal(value, settings.TxDelayMs);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void TxTailMs_Negative_Throws(int value)
    {
        AfskSettings settings = new();
        Assert.Throws<ArgumentOutOfRangeException>(() => settings.TxTailMs = value);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(50)]
    public void TxTailMs_NonNegative_IsAccepted(int value)
    {
        AfskSettings settings = new() { TxTailMs = value };
        Assert.Equal(value, settings.TxTailMs);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void SlotTimeMs_Negative_Throws(int value)
    {
        AfskSettings settings = new();
        Assert.Throws<ArgumentOutOfRangeException>(() => settings.SlotTimeMs = value);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    public void SlotTimeMs_NonNegative_IsAccepted(int value)
    {
        AfskSettings settings = new() { SlotTimeMs = value };
        Assert.Equal(value, settings.SlotTimeMs);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(256)]
    public void Persist_OutsideByteRange_Throws(int value)
    {
        AfskSettings settings = new();
        Assert.Throws<ArgumentOutOfRangeException>(() => settings.Persist = value);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(63)]
    [InlineData(255)]
    public void Persist_WithinByteRange_IsAccepted(int value)
    {
        AfskSettings settings = new() { Persist = value };
        Assert.Equal(value, settings.Persist);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-0.5)]
    [InlineData(1.5)]
    public void AudioLevel_OutsideOpenUnitInterval_Throws(double value)
    {
        AfskSettings settings = new();
        Assert.Throws<ArgumentOutOfRangeException>(() => settings.AudioLevel = value);
    }

    [Theory]
    [InlineData(0.1)]
    [InlineData(0.9)]
    [InlineData(1.0)]
    public void AudioLevel_WithinOpenUnitInterval_IsAccepted(double value)
    {
        AfskSettings settings = new() { AudioLevel = value };
        Assert.Equal(value, settings.AudioLevel);
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    public void SquelchThreshold_OutsideClosedUnitInterval_Throws(double value)
    {
        AfskSettings settings = new();
        Assert.Throws<ArgumentOutOfRangeException>(() => settings.SquelchThreshold = value);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.1)]
    [InlineData(1.0)]
    public void SquelchThreshold_WithinClosedUnitInterval_IsAccepted(double value)
    {
        AfskSettings settings = new() { SquelchThreshold = value };
        Assert.Equal(value, settings.SquelchThreshold);
    }
}
