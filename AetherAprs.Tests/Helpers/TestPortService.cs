// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Services.Diagnostics;
using AetherAprs.Services.Packets;
using AetherAprs.Services.Platform;
using AetherAprs.Services.Ports;
using Microsoft.Extensions.Logging.Abstractions;

namespace AetherAprs.Tests.Helpers;

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
        IPortConfigurationService configuration = StoreFactory.CreatePortConfigurationService(
            database,
            collaborators.SecretProtector);

        PacketIngestPipeline ingest = new(
            collaborators.PacketStorageService,
            NullLogger<PacketIngestPipeline>.Instance,
            collaborators.DiagnosticSink,
            collaborators.IngestOptions);
        DigipeatRouter digipeat = new(
            configuration,
            sessionManager,
            collaborators.DigipeaterService,
            NullLogger<DigipeatRouter>.Instance,
            collaborators.DiagnosticSink,
            collaborators.DigipeatOptions);

        // The composition root starts these consumers in its startup phase; a test
        // that builds the runtime directly starts them the same way so packet flow
        // is live. StartAsync completes synchronously, so no await is needed here.
        ingest.StartAsync().GetAwaiter().GetResult();
        digipeat.StartAsync().GetAwaiter().GetResult();

        return new PortService(
            configuration,
            sessionManager,
            ingest,
            digipeat,
            NullLogger<PortService>.Instance,
            collaborators.KeepAliveService,
            collaborators.PermissionPrompter);
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

        public IPortPermissionPrompter? PermissionPrompter { get; init; }

        public IDiagnosticEventSink? DiagnosticSink { get; init; }

        public PacketIngestOptions? IngestOptions { get; init; }

        public DigipeatRouterOptions? DigipeatOptions { get; init; }

        public ISecretProtector? SecretProtector { get; init; }
    }
}
