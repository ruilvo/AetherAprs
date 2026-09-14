// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using AetherAprs.Configuration;
using AetherAprs.Extensions;
using Xunit;

namespace AetherAprs.Tests.Extensions;

public sealed class PortConfigExtensionsTests
{
    [Fact]
    public void GetAprsIsSettings_ReturnsNullForNonAprsIsPort()
    {
        var port = new PortConfig { Name = "RF", TypeSettings = new KissSettings() };
        Assert.Null(port.GetAprsIsSettings());
    }

    [Fact]
    public void GetAprsIsSettingsOrThrow_ThrowsForNonAprsIsPort()
    {
        var port = new PortConfig { Name = "RF", TypeSettings = new KissSettings() };
        Assert.Throws<InvalidOperationException>(() => port.GetAprsIsSettingsOrThrow());
    }

    [Fact]
    public void EnsureAprsIsSettings_CreatesDefaultsWhenMissing()
    {
        var port = new PortConfig { Name = "IS" };
        var settings = port.EnsureAprsIsSettings();
        Assert.Same(settings, port.TypeSettings);
        Assert.Same(settings, port.GetAprsIsSettingsOrThrow());
    }

    [Fact]
    public void EnsureKissSettings_CreatesDefaultTcpTransportWhenMissing()
    {
        var port = new PortConfig { Name = "RF" };
        var settings = port.EnsureKissSettings();
        Assert.IsType<TcpKissTransportSettings>(settings.Transport);
        Assert.Same(settings, port.GetKissSettingsOrThrow());
    }

    [Fact]
    public void GetKissSettingsOrThrow_ThrowsForAprsIsPort()
    {
        var port = new PortConfig { Name = "IS", TypeSettings = new AprsIsSettings() };
        Assert.Throws<InvalidOperationException>(() => port.GetKissSettingsOrThrow());
    }
}
