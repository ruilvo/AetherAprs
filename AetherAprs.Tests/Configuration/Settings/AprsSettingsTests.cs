// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using AetherAprs.Configuration.Settings;
using Xunit;

namespace AetherAprs.Tests.Configuration.Settings;

public sealed class AprsSettingsTests
{
    [Fact]
    public void AprsSettings_CallsignEmpty_Throws()
    {
        var settings = new AprsSettings();

        Assert.Throws<ArgumentException>(() => settings.Callsign = string.Empty);
    }
}
