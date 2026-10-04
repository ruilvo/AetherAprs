// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Data.Entities;
using AetherAprs.Data.Mappers;
using AetherAprs.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Linq;
using System.Threading;

namespace AetherAprs.Data;

/// <summary>
/// Applies EF migrations and seeds default data on first run.
/// </summary>
public sealed class AppSavedDataInitializer(
    IDbContextFactory<AppDbContext> dbContextFactory,
    ILogger<AppSavedDataInitializer> logger)
{
    private readonly Lock _gate = new();
    private bool _initialized;

    public void Initialize()
    {
        lock (_gate)
        {
            if (_initialized)
            {
                return;
            }

            logger.LogInformation("Initializing database and seeding default data");
            using var db = dbContextFactory.CreateDbContext();
            db.Database.Migrate();
            SeedBeaconingDefaults(db);
            _initialized = true;
            logger.LogInformation("Database initialization complete");
        }
    }

    private void SeedBeaconingDefaults(AppDbContext db)
    {
        if (!db.BeaconConfigs.Any())
        {
            logger.LogInformation("Seeding default beacon configurations");
            db.BeaconConfigs.AddRange(
                BeaconConfigMapper.ToRecord(BeaconConfig.CreateWalkPreset()),
                BeaconConfigMapper.ToRecord(BeaconConfig.CreateDrivePreset()),
                BeaconConfigMapper.ToRecord(BeaconConfig.CreateCustomPreset()));
        }

        if (!db.BeaconingState.Any())
        {
            logger.LogInformation("Creating default beaconing state");
            db.BeaconingState.Add(new BeaconingStateRecord());
        }

        db.SaveChanges();
    }
}
