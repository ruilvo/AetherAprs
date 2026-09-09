// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Models.Aprs;
using Xunit;

namespace AetherAprs.Tests.Models;

public class PacketTypeTests
{
    [Fact]
    public void Enum_HasPosition()
    {
        Assert.Equal(0, (int)PacketType.Position);
    }

    [Fact]
    public void Enum_HasMessage()
    {
        Assert.Equal(1, (int)PacketType.Message);
    }

    [Fact]
    public void Enum_HasStatus()
    {
        Assert.Equal(2, (int)PacketType.Status);
    }

    [Fact]
    public void Enum_HasWeather()
    {
        Assert.Equal(3, (int)PacketType.Weather);
    }

    [Fact]
    public void Enum_HasUnknown()
    {
        Assert.Equal(4, (int)PacketType.Unknown);
    }
}