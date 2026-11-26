// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Services.Diagnostics;
using AetherAprs.Services.Packets;
using AetherAprs.Services.Platform;
using AetherAprs.Services.Ports;
using AetherAprs.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;

namespace AetherAprs.Tests;

/// <summary>
/// Builds the real port runtime over a temporary database: configuration owner,
/// session manager, ingest queue and forwarding queue, wired the way the
/// composition root wires them.
/// </summary>
internal static class TestPortService
{
    public static PortService Create(
        TempAppDatabase database,
        IPortSessionManager sessionManager,
        PortServiceCollaborators collaborators)
    {
        var configuration = StoreFactory.CreatePortConfigurationService(
            database,
            collaborators.SecretProtector);

        return new PortService(
            configuration,
            sessionManager,
            new PacketIngestPipeline(
                collaborators.PacketStorageService,
                NullLogger<PacketIngestPipeline>.Instance,
                collaborators.DiagnosticSink,
                collaborators.IngestOptions),
            new DigipeatRouter(
                configuration,
                sessionManager,
                collaborators.DigipeaterService,
                NullLogger<DigipeatRouter>.Instance,
                collaborators.DiagnosticSink,
                collaborators.DigipeatOptions),
            NullLogger<PortService>.Instance,
            collaborators.KeepAliveService);
    }

    /// <summary>
    /// Everything <see cref="PortService"/> needs besides the session manager. A
    /// test overrides only the collaborator it cares about.
    /// </summary>
    internal sealed class PortServiceCollaborators
    {
        public required IPacketStorageService PacketStorageService { get; init; }

        public required IDigipeaterService DigipeaterService { get; init; }

        public required IKeepAliveService KeepAliveService { get; init; }

        public IDiagnosticEventSink? DiagnosticSink { get; init; }

        public PacketIngestOptions? IngestOptions { get; init; }

        public DigipeatRouterOptions? DigipeatOptions { get; init; }

        public ISecretProtector? SecretProtector { get; init; }
    }
}
