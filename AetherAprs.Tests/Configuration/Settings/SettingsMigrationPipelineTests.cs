// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;

using AetherAprs.Configuration.Settings;
using AetherAprs.Configuration.Settings.Migrations;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Xunit;

namespace AetherAprs.Tests.Configuration.Settings;

/// <summary>
/// Tests for the settings migration pipeline itself, using synthetic migration
/// chains so the version arithmetic and failure handling are covered without
/// depending on the migrations shipped by this build.
/// </summary>
public sealed class SettingsMigrationPipelineTests
{
    private static readonly ILogger Logger = NullLogger.Instance;

    [Fact]
    public void Migrate_AtTargetVersion_ChangesNothing()
    {
        Dictionary<string, string?> values = new()
        { ["A"] = "1" };

        int result = SettingsMigrationPipeline.Migrate(values, 3, 3, [], Logger);

        Assert.Equal(3, result);
        Assert.Single(values);
        Assert.Equal("1", values["A"]);
    }

    [Fact]
    public void Migrate_AboveTargetVersion_DoesNotDowngrade()
    {
        Dictionary<string, string?> values = new()
        { ["A"] = "1" };

        int result = SettingsMigrationPipeline.Migrate(
            values,
            fromVersion: 7,
            targetVersion: 2,
            migrations: [new RecordingMigration(0)],
            Logger);

        Assert.Equal(7, result);
        Assert.Single(values);
    }

    [Fact]
    public void Migrate_AppliesEveryMigrationInOrder()
    {
        Dictionary<string, string?> values = [];
        RecordingMigration first = new(0);
        RecordingMigration second = new(1);
        RecordingMigration third = new(2);

        int result = SettingsMigrationPipeline.Migrate(
            values,
            fromVersion: 0,
            targetVersion: 3,
            migrations: [first, second, third],
            Logger);

        Assert.Equal(3, result);
        Assert.True(first.Applied);
        Assert.True(second.Applied);
        Assert.True(third.Applied);
    }

    [Fact]
    public void Migrate_PicksMigrationsByFromVersion_NotListOrder()
    {
        Dictionary<string, string?> values = [];
        RecordingMigration second = new(1);
        RecordingMigration first = new(0);

        int result = SettingsMigrationPipeline.Migrate(
            values,
            fromVersion: 0,
            targetVersion: 2,
            migrations: [second, first],
            Logger);

        Assert.Equal(2, result);
        Assert.True(first.Applied);
        Assert.True(second.Applied);
    }

    [Fact]
    public void Migrate_StopsAtGapAndReportsReachedVersion()
    {
        Dictionary<string, string?> values = [];
        RecordingMigration first = new(0);

        // No migration from version 1, so the run must stop after version 0.
        int result = SettingsMigrationPipeline.Migrate(
            values,
            fromVersion: 0,
            targetVersion: 3,
            migrations: [first],
            Logger);

        Assert.Equal(1, result);
        Assert.True(first.Applied);
    }

    [Fact]
    public void Migrate_StartingFromLegacyVersion_RunsChainFromZero()
    {
        Dictionary<string, string?> values = [];
        RecordingMigration migration = new(SettingsSchema.LegacyUnversionedVersion);

        int result = SettingsMigrationPipeline.Migrate(
            values,
            fromVersion: SettingsSchema.LegacyUnversionedVersion,
            targetVersion: 1,
            migrations: [migration],
            Logger);

        Assert.Equal(1, result);
        Assert.True(migration.Applied);
    }

    [Fact]
    public void Migrate_DoesNotReapplyMigrationOnSecondRun()
    {
        Dictionary<string, string?> values = [];
        RecordingMigration migration = new(0);

        int first = SettingsMigrationPipeline.Migrate(values, 0, 1, [migration], Logger);
        int second = SettingsMigrationPipeline.Migrate(values, first, 1, [migration], Logger);

        Assert.Equal(1, first);
        Assert.Equal(1, second);
        Assert.Equal(1, migration.ApplyCount);
    }

    [Fact]
    public void Migrate_MigrationCanMutateValues()
    {
        Dictionary<string, string?> values = new()
        { ["OldKey"] = "legacy" };
        RecordingMigration migration = new(0, v =>
        {
            v.Remove("OldKey");
            v["NewKey"] = "modern";
        });

        SettingsMigrationPipeline.Migrate(values, 0, 1, [migration], Logger);

        Assert.False(values.ContainsKey("OldKey"));
        Assert.Equal("modern", values["NewKey"]);
    }

    [Fact]
    public void Migrate_EmptyMigrationsFromLegacy_LeavesVersionUnchanged()
    {
        Dictionary<string, string?> values = new()
        { ["A"] = "1" };

        int result = SettingsMigrationPipeline.Migrate(values, 0, 1, [], Logger);

        Assert.Equal(0, result);
        Assert.Equal("1", values["A"]);
    }

    private sealed class RecordingMigration(
        int fromVersion,
        Action<Dictionary<string, string?>>? mutate = null) : ISettingsMigration
    {
        public int FromVersion => fromVersion;

        public bool Applied => ApplyCount > 0;

        public int ApplyCount { get; private set; }

        public void Apply(IDictionary<string, string?> values, ILogger logger)
        {
            ApplyCount++;
            if (mutate is not null && values is Dictionary<string, string?> typed)
            {
                mutate(typed);
            }
        }
    }
}
