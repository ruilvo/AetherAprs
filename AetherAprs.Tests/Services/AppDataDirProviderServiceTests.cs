// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using AetherAprs.Services;
using Xunit;

namespace AetherAprs.Tests.Services;

public sealed class AppDataDirProviderServiceTests
{
    [Fact]
    public void GetAppDataDirectory_ReturnsAppContextBaseDirectory()
    {
        var provider = new AppDataDirProviderService();

        Assert.Equal(AppContext.BaseDirectory, provider.GetAppDataDirectory());
    }
}
