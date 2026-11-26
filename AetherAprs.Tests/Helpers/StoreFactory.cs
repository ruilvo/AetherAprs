// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Data;
using AetherAprs.Data.Beaconing;
using AetherAprs.Data.Ports;
using AetherAprs.Services.Beaconing;
using AetherAprs.Services.Platform;
using AetherAprs.Services.Ports;
using System;

namespace AetherAprs.Tests.Helpers;

/// <summary>
/// Builds the real durable stores over a temporary database so tests exercise the
/// production adapter instead of hand-written persistence or a mock of it.
/// </summary>
internal static class StoreFactory
{
    /// <summary>Creates a port registry backed by <paramref name="database"/>.</summary>
    public static IPortRegistry CreatePortRegistry(
        TempAppDatabase database, ISecretProtector? secretProtector = null) =>
        new PortRegistry(
            new SqliteRuntimeWriter(database.Factory),
            new SqliteRuntimeReader(database.Factory),
            secretProtector ?? new PlaintextSecretProtector(
                Microsoft.Extensions.Logging.Abstractions.NullLogger<PlaintextSecretProtector>.Instance),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<PortRegistry>.Instance);

    /// <summary>Creates a beacon state store backed by <paramref name="database"/>.</summary>
    public static IBeaconStateStore CreateBeaconStateStore(TempAppDatabase database) =>
        new BeaconStateStore(
            new SqliteRuntimeWriter(database.Factory),
            new SqliteRuntimeReader(database.Factory));

    /// <summary>
    /// Creates the single owner of configured ports, backed by
    /// <paramref name="database"/>.
    /// </summary>
    public static IPortConfigurationService CreatePortConfigurationService(
        TempAppDatabase database, ISecretProtector? secretProtector = null) =>
        new PortConfigurationService(CreatePortRegistry(database, secretProtector));
}
