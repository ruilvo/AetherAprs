// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Linq;

using AetherAprs.Data;
using AetherAprs.Models;
using AetherAprs.Tests.Helpers;

using Microsoft.Extensions.Logging.Abstractions;

using Xunit;

namespace AetherAprs.Tests.Data;

public sealed class AppSavedDataInitializerTests
{
    [Fact]
    public void InitializeSeedsBeaconingDefaults()
    {
        using TempAppDatabase db = TempAppDatabase.CreateEmpty();

        AppSavedDataInitializer initializer = new(
            db.Factory,
            NullLogger<AppSavedDataInitializer>.Instance);

        initializer.Initialize();
        initializer.Initialize(); // Second call should be idempotent

        using AppDbContext context = db.CreateContext();
        Assert.Equal(3, context.BeaconConfigs.Count());
        Assert.Equal(DynamicBeaconMode.Walk, Assert.Single(context.BeaconingState).ActiveMode);
    }

    [Fact]
    public void InitializeDoesNotDuplicateBeaconingData()
    {
        using TempAppDatabase db = TempAppDatabase.CreateEmpty();

        new AppSavedDataInitializer(
            db.Factory,
            NullLogger<AppSavedDataInitializer>.Instance).Initialize();

        new AppSavedDataInitializer(
            db.Factory,
            NullLogger<AppSavedDataInitializer>.Instance).Initialize();

        using AppDbContext reloaded = db.CreateContext();
        Assert.Equal(3, reloaded.BeaconConfigs.Count());
        Assert.Single(reloaded.BeaconingState);
    }
}
