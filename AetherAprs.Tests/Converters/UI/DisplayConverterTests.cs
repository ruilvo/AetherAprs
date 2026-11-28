// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Globalization;

using AetherAprs.Configuration;
using AetherAprs.Converters.UI;
using AetherAprs.Localization;

using Avalonia.Data;
using Avalonia.Data.Converters;

using Xunit;

namespace AetherAprs.Tests.Converters.UI;

public sealed class DisplayConverterTests
{
    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    [Fact]
    public void RssiFormatConverter_FormatsRssi()
    {
        RssiFormatConverter converter = new();

        object? result = converter.Convert(-65, typeof(string), null, Culture);

        Assert.Equal(string.Format(Culture, Strings.Get("RssiFormat", Culture), -65), result);
    }

    [Fact]
    public void PacketDisplayTimeRangeConverter_FormatsKnownRange()
    {
        PacketDisplayTimeRangeConverter converter = new();

        object? result = converter.Convert(PacketDisplayTimeRange.LastWeek, typeof(string), null, Culture);

        Assert.Equal(Strings.Get("TimeRangeLastWeek", Culture), result);
    }

    [Fact]
    public void ThemeModeConverter_FormatsKnownMode()
    {
        ThemeModeConverter converter = new();

        object? result = converter.Convert(ThemeMode.Dark, typeof(string), null, Culture);

        Assert.Equal(Strings.Get("ThemeModeDark", Culture), result);
    }

    [Fact]
    public void TileSourceTypeConverter_FormatsKnownSource()
    {
        TileSourceTypeConverter converter = new();

        object? result = converter.Convert(TileSourceType.MbTilesOffline, typeof(string), null, Culture);

        Assert.Equal(Strings.Get("TileSourceMbTilesOffline", Culture), result);
    }

    [Fact]
    public void TypeNameToIsVisibleConverter_MatchesTypeName()
    {
        TypeNameToIsVisibleConverter converter = new();

        object? result = converter.Convert(typeof(TestType), typeof(bool), "TestType", Culture);

        Assert.Equal(true, result);
    }

    [Fact]
    public void TypeNameToIsVisibleConverter_DoesNotMatchDifferentTypeName()
    {
        TypeNameToIsVisibleConverter converter = new();

        object? result = converter.Convert(typeof(TestType), typeof(bool), "OtherType", Culture);

        Assert.Equal(false, result);
    }

    [Fact]
    public void EnumDisplayConverter_ForwardsAfskPortType()
    {
        EnumDisplayConverter converter = new();

        Assert.Equal(Strings.Get("PortTypeAfsk"), converter.Convert(typeof(AfskSettings), typeof(string), null, Culture));
        Assert.Equal(typeof(AfskSettings), converter.ConvertBack(Strings.Get("PortTypeAfsk"), typeof(Type), null, Culture));
        Assert.Equal(typeof(AfskSettings), converter.ConvertBack("AFSK", typeof(Type), null, Culture));
    }

    [Theory]
    [InlineData(PttMode.Vox, "PttModeVox")]
    [InlineData(PttMode.SpeakerMic, "PttModeSpeakerMic")]
    public void EnumDisplayConverter_ForwardsPttMode(PttMode mode, string key)
    {
        EnumDisplayConverter converter = new();

        Assert.Equal(Strings.Get(key), converter.Convert(mode, typeof(string), null, Culture));
    }

    [Theory]
    [InlineData(typeof(RssiFormatConverter))]
    [InlineData(typeof(PacketDisplayTimeRangeConverter))]
    [InlineData(typeof(ThemeModeConverter))]
    [InlineData(typeof(TileSourceTypeConverter))]
    [InlineData(typeof(TypeNameToIsVisibleConverter))]
    public void DisplayConverters_ConvertBack_DoNothing(Type converterType)
    {
        IValueConverter converter = (IValueConverter)Activator.CreateInstance(converterType)!;

        object? result = converter.ConvertBack("display text", typeof(object), null, Culture);

        Assert.Same(BindingOperations.DoNothing, result);
    }

    private sealed class TestType
    {
    }
}
