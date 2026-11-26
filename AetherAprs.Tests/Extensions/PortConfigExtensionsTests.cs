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
        PortConfig port = new() { Name = "RF", TypeSettings = new KissSettings() };
        Assert.Null(port.GetAprsIsSettings());
    }

    [Fact]
    public void GetAprsIsSettingsOrThrow_ThrowsForNonAprsIsPort()
    {
        PortConfig port = new() { Name = "RF", TypeSettings = new KissSettings() };
        Assert.Throws<InvalidOperationException>(() => port.GetAprsIsSettingsOrThrow());
    }

    [Fact]
    public void EnsureAprsIsSettings_CreatesDefaultsWhenMissing()
    {
        PortConfig port = new() { Name = "IS" };
        AprsIsSettings settings = port.EnsureAprsIsSettings();
        Assert.Same(settings, port.TypeSettings);
        Assert.Same(settings, port.GetAprsIsSettingsOrThrow());
    }

    [Fact]
    public void EnsureKissSettings_CreatesDefaultTcpTransportWhenMissing()
    {
        PortConfig port = new() { Name = "RF" };
        KissSettings settings = port.EnsureKissSettings();
        Assert.IsType<TcpKissTransportSettings>(settings.Transport);
        Assert.Same(settings, port.GetKissSettingsOrThrow());
    }

    [Fact]
    public void GetKissSettingsOrThrow_ThrowsForAprsIsPort()
    {
        PortConfig port = new() { Name = "IS", TypeSettings = new AprsIsSettings() };
        Assert.Throws<InvalidOperationException>(() => port.GetKissSettingsOrThrow());
    }
}
