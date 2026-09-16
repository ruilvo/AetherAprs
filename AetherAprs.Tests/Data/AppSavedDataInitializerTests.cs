// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.IO;
using System.Linq;
using AetherAprs.Configuration;
using AetherAprs.Data;
using AetherAprs.Models;
using AetherAprs.Services;
using AetherAprs.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AetherAprs.Tests.Data;

public sealed class AppSavedDataInitializerTests
{
    [Fact]
    public void InitializeImportsLegacyPortsAndSeedsBeaconing()
    {
        using var db = TempAppDatabase.CreateEmpty();
        File.WriteAllText(
            Path.Combine(db.Directory, "appsettings.json"),
            """
            {
              "Aprs": { "Callsign": "N0CALL" },
              "Ports": [
                {
                  "Name": "APRS-IS",
                  "TypeSettings": {
                    "$type": "aprs-is",
                    "Server": "rotate.aprs2.net",
                    "ServerPort": 14580,
                    "Passcode": "12345",
                    "Filter": "m/50"
                  }
                }
              ]
            }
            """);
        File.WriteAllText(
            Path.Combine(db.Directory, "appsettings.Development.json"),
            """
            {
              "Logging": { "LogLevel": { "Default": "Debug" } }
            }
            """);

        var initializer = new AppSavedDataInitializer(
            db.Factory,
            new TestAppDataDirProvider(db.Directory),
            NullLogger<AppSavedDataInitializer>.Instance);

        initializer.Initialize();
        initializer.Initialize();

        using var context = db.CreateContext();
        var port = PortRecordMapper.ToConfig(Assert.Single(context.Ports.AsNoTracking()));
        Assert.Equal("APRS-IS", port.Name);
        var aprsIs = Assert.IsType<AprsIsSettings>(port.TypeSettings);
        Assert.Equal("rotate.aprs2.net", aprsIs.Server);
        Assert.Equal(3, context.BeaconConfigs.Count());
        Assert.Equal(DynamicBeaconMode.Walk, Assert.Single(context.BeaconingState).ActiveMode);

        var json = File.ReadAllText(Path.Combine(db.Directory, "appsettings.json"));
        Assert.DoesNotContain("Ports", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InitializeDoesNotImportWhenPortsAlreadyExist()
    {
        using var db = TempAppDatabase.CreateEmpty();
        File.WriteAllText(Path.Combine(db.Directory, "appsettings.json"), "{}");

        new AppSavedDataInitializer(
            db.Factory,
            new TestAppDataDirProvider(db.Directory),
            NullLogger<AppSavedDataInitializer>.Instance).Initialize();

        using var context = db.CreateContext();
        context.Ports.Add(PortRecordMapper.ToRecord(new PortConfig { Id = Guid.NewGuid(), Name = "Existing" }));
        context.SaveChanges();

        File.WriteAllText(
            Path.Combine(db.Directory, "appsettings.json"),
            """
            {
              "Ports": [ { "Name": "Legacy" } ]
            }
            """);

        new AppSavedDataInitializer(
            db.Factory,
            new TestAppDataDirProvider(db.Directory),
            NullLogger<AppSavedDataInitializer>.Instance).Initialize();

        using var reloaded = db.CreateContext();
        var port = Assert.Single(reloaded.Ports.AsNoTracking());
        Assert.Equal("Existing", port.Name);
        var json = File.ReadAllText(Path.Combine(db.Directory, "appsettings.json"));
        Assert.DoesNotContain("Ports", json, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class TestAppDataDirProvider(string directory) : IAppDataDirProviderService
    {
        public string GetAppDataDirectory() => directory;
    }
}
