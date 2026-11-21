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
        var converter = new RssiFormatConverter();

        var result = converter.Convert(-65, typeof(string), null, Culture);

        Assert.Equal(string.Format(Culture, Strings.Get("RssiFormat", Culture), -65), result);
    }

    [Fact]
    public void PacketDisplayTimeRangeConverter_FormatsKnownRange()
    {
        var converter = new PacketDisplayTimeRangeConverter();

        var result = converter.Convert(PacketDisplayTimeRange.LastWeek, typeof(string), null, Culture);

        Assert.Equal(Strings.Get("TimeRangeLastWeek", Culture), result);
    }

    [Fact]
    public void ThemeModeConverter_FormatsKnownMode()
    {
        var converter = new ThemeModeConverter();

        var result = converter.Convert(ThemeMode.Dark, typeof(string), null, Culture);

        Assert.Equal(Strings.Get("ThemeModeDark", Culture), result);
    }

    [Fact]
    public void TileSourceTypeConverter_FormatsKnownSource()
    {
        var converter = new TileSourceTypeConverter();

        var result = converter.Convert(TileSourceType.MbTilesOffline, typeof(string), null, Culture);

        Assert.Equal(Strings.Get("TileSourceMbTilesOffline", Culture), result);
    }

    [Fact]
    public void TypeNameToIsVisibleConverter_MatchesTypeName()
    {
        var converter = new TypeNameToIsVisibleConverter();

        var result = converter.Convert(typeof(TestType), typeof(bool), "TestType", Culture);

        Assert.Equal(true, result);
    }

    [Fact]
    public void TypeNameToIsVisibleConverter_DoesNotMatchDifferentTypeName()
    {
        var converter = new TypeNameToIsVisibleConverter();

        var result = converter.Convert(typeof(TestType), typeof(bool), "OtherType", Culture);

        Assert.Equal(false, result);
    }

    [Theory]
    [InlineData(typeof(RssiFormatConverter))]
    [InlineData(typeof(PacketDisplayTimeRangeConverter))]
    [InlineData(typeof(ThemeModeConverter))]
    [InlineData(typeof(TileSourceTypeConverter))]
    [InlineData(typeof(TypeNameToIsVisibleConverter))]
    public void DisplayConverters_ConvertBack_DoNothing(Type converterType)
    {
        var converter = (IValueConverter)Activator.CreateInstance(converterType)!;

        var result = converter.ConvertBack("display text", typeof(object), null, Culture);

        Assert.Same(BindingOperations.DoNothing, result);
    }

    private sealed class TestType
    {
    }
}
