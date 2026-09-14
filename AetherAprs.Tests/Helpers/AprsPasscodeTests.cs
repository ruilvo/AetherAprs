// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using AetherAprs.Helpers;
using Xunit;

namespace AetherAprs.Tests.Helpers;

public sealed class AprsPasscodeTests
{
    [Theory]
    [InlineData("N0CALL", "13023")]
    [InlineData("n0call", "13023")]
    [InlineData("TEST", "29939")]
    public void Compute_ReturnsKnownPasscodes(string callsign, string expected)
    {
        Assert.Equal(expected, AprsPasscode.Compute(callsign));
    }

    [Fact]
    public void Compute_ThrowsOnNullOrWhitespace()
    {
        Assert.Throws<ArgumentException>(() => AprsPasscode.Compute(" "));
        Assert.Throws<ArgumentNullException>(() => AprsPasscode.Compute(null!));
    }
}
