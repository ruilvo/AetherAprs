// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Globalization;
using AetherAprs.Converters.UI;
using Xunit;

namespace AetherAprs.Tests.Converters.UI;

public sealed class DateTimeToLocalConverterTests
{
    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;
    private static readonly DateTimeToLocalConverter Converter = new();

    [Fact]
    public void Convert_UtcDateTime_ConvertsToLocalTime()
    {
        var utc = new DateTime(2026, 11, 23, 12, 30, 0, DateTimeKind.Utc);

        var result = Assert.IsType<DateTime>(Converter.Convert(utc, typeof(DateTime), null, Culture));

        Assert.Equal(DateTimeKind.Local, result.Kind);
        Assert.Equal(utc, result.ToUniversalTime());
    }

    [Fact]
    public void Convert_UnspecifiedDateTime_TreatsValueAsUtc()
    {
        // SQLite materialises timestamps as DateTimeKind.Unspecified; the data
        // layer stores UTC, so the converter must localise rather than pass through.
        var storedUtc = new DateTime(2026, 11, 23, 12, 30, 0, DateTimeKind.Unspecified);

        var result = Assert.IsType<DateTime>(Converter.Convert(storedUtc, typeof(DateTime), null, Culture));

        Assert.Equal(DateTimeKind.Local, result.Kind);
        Assert.Equal(TimeZoneInfo.ConvertTimeFromUtc(storedUtc, TimeZoneInfo.Local), result);
    }

    [Fact]
    public void Convert_LocalDateTime_ReturnsUnchanged()
    {
        var local = new DateTime(2026, 11, 23, 12, 30, 0, DateTimeKind.Local);

        var result = Assert.IsType<DateTime>(Converter.Convert(local, typeof(DateTime), null, Culture));

        Assert.Equal(local, result);
        Assert.Equal(DateTimeKind.Local, result.Kind);
    }

    [Fact]
    public void Convert_DateTimeOffset_ConvertsToLocalTime()
    {
        var offset = new DateTimeOffset(2026, 11, 23, 12, 30, 0, TimeSpan.FromHours(2));

        var result = Assert.IsType<DateTimeOffset>(Converter.Convert(offset, typeof(DateTimeOffset), null, Culture));

        Assert.Equal(offset.UtcDateTime, result.UtcDateTime);
        Assert.Equal(TimeZoneInfo.Local.GetUtcOffset(offset.UtcDateTime), result.Offset);
    }

    [Fact]
    public void Convert_NonDateValue_ReturnsUnchanged()
    {
        Assert.Equal("not a date", Converter.Convert("not a date", typeof(object), null, Culture));
        Assert.Null(Converter.Convert(null, typeof(object), null, Culture));
    }

    [Fact]
    public void ConvertBack_Throws()
    {
        Assert.Throws<NotSupportedException>(() => Converter.ConvertBack(DateTime.UtcNow, typeof(DateTime), null, Culture));
    }
}
