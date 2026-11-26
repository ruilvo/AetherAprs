// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Linq;

namespace AetherAprs.Tests.Architecture;

/// <summary>
/// Enforces the persistence and presentation ownership boundaries described in
/// the architecture roadmap (work item R0.2).
/// </summary>
/// <remarks>
/// The scan runs over the production source tree rather than compiled metadata so
/// it reports the exact symbol that broke a rule. Comments and string literal
/// contents are stripped first: code is the only thing that creates a dependency.
/// </remarks>
internal static class ArchitectureBoundaryScanner
{
    /// <summary>
    /// Files permitted to open a database context or mutate persisted state. These
    /// are the data infrastructure and the composition root, not feature services.
    /// </summary>
    private static readonly HashSet<string> PersistenceOwners = new(StringComparer.Ordinal)
    {
        "AetherAprs/Data/AppDbContext.cs",
        "AetherAprs/Data/AppDbContextFactory.cs",
        "AetherAprs/Data/AppSavedDataInitializer.cs",
        "AetherAprs/Data/SqliteRuntimeRegistration.cs",
        "AetherAprs/Data/SqliteRuntimeReader.cs",
        "AetherAprs/Data/SqliteRuntimeWriter.cs",
        "AetherAprs/Data/Packets/PacketQueryService.cs",
        "AetherAprs/Data/Packets/PacketStorageService.cs",
        "AetherAprs/Data/Ports/PortRegistry.cs",
        "AetherAprs/Data/Beaconing/BeaconStateStore.cs",
        "AetherAprs/Factories/ServiceProviderFactory.cs",
        "AetherAprs/DesignData.cs"
    };

    /// <summary>
    /// Files permitted to marshal work onto the Avalonia UI thread. These are the
    /// presentation adapters for that concern.
    /// </summary>
    private static readonly HashSet<string> UiThreadOwners = new(StringComparer.Ordinal)
    {
        "AetherAprs/Services/UI/AvaloniaUiDispatcher.cs",
        "AetherAprs/Services/UI/SnackbarTransientFeedbackService.cs",
        "AetherAprs/Behaviors/ScrollToFocusedControlBehavior.cs"
    };

    /// <summary>
    /// Files permitted to construct a modem. Transport selection belongs to the
    /// modem factory, so no session or feature code may pick a transport itself.
    /// </summary>
    private static readonly HashSet<string> ModemConstructionOwners = new(StringComparer.Ordinal)
    {
        "AetherAprs/Services/Ports/ModemFactory.cs"
    };

    /// <summary>
    /// Files permitted to name a port modem. Sessions are owned by the session
    /// manager, so no caller can hold, start, stop, or dispose a modem instance.
    /// </summary>
    private static readonly HashSet<string> ModemAccessOwners = new(StringComparer.Ordinal)
    {
        "AetherAprs/Services/Ports/IModemFactory.cs",
        "AetherAprs/Services/Ports/ModemFactory.cs",
        "AetherAprs/Services/Ports/PortSessionManager.cs"
    };

    /// <summary>
    /// Files permitted to create a bounded work queue. A queue is a resource with a
    /// consumer and a drop policy, so it needs a named owner rather than being
    /// created wherever a burst happens to arrive.
    /// </summary>
    private static readonly HashSet<string> BoundedQueueOwners = new(StringComparer.Ordinal)
    {
        "AetherAprs/Data/SqliteRuntimeWriter.cs",
        "AetherAprs/Services/Packets/PacketIngestPipeline.cs",
        "AetherAprs/Services/Ports/DigipeatRouter.cs",
        "AetherAprs/Services/Transmission/BeaconTransmitQueue.cs",
        "AetherAprs.Android/Services/Bluetooth/AndroidBluetoothLeScanner.cs",
        "AetherAprs.Android/Services/Transports/BluetoothLeGattDuplexStream.cs"
    };

    /// <summary>
    /// Files permitted to name the platform foreground service. Keep-alive state
    /// is serialized by the keep-alive service, so feature code holds a reason
    /// instead of starting or stopping the platform service itself.
    /// </summary>
    private static readonly HashSet<string> ForegroundServiceOwners = new(StringComparer.Ordinal)
    {
        "AetherAprs/Services/Platform/IForegroundService.cs",
        "AetherAprs/Services/Platform/NoOpForegroundService.cs",
        "AetherAprs/Services/Platform/KeepAliveService.cs",
        "AetherAprs.Android/Services/Platform/AndroidForegroundService.cs",
        "AetherAprs.Android/Application.cs",
        "AetherAprs/Factories/ServiceProviderFactory.cs",
        "AetherAprs/DesignData.cs"
    };

    /// <summary>Every rule, in the order it is reported.</summary>
    public static IReadOnlyList<ArchitectureRule> Rules { get; } =
    [
        new ArchitectureRule(
            "A001",
            "Feature and service code must not open a database context or write directly; use the runtime writer and query services.",
            [
                "IDbContextFactory", "AppDbContext", "DbSet<", "Set<", "SaveChanges", "ExecuteDelete", "ExecuteUpdate"
            ],
            PersistenceOwners),

        new ArchitectureRule(
            "A002",
            "Application and service code must not dispatch through Avalonia; inject IUiDispatcher instead.",
            ["Dispatcher.UIThread", "Avalonia.Threading"],
            UiThreadOwners),

        new ArchitectureRule(
            "A003",
            "Production code must log structured events through ILogger, not Debug.WriteLine.",
            ["Debug.WriteLine"],
            new HashSet<string>(StringComparer.Ordinal)),

        new ArchitectureRule(
            "A004",
            "Transport selection belongs to IModemFactory; session and feature code must not construct a modem.",
            ["new AprsIsModem", "new AprsRfModem", "new KissModem", "new AfskModem"],
            ModemConstructionOwners),

        new ArchitectureRule(
            "A005",
            "Running modem sessions are owned by IPortSessionManager; only the factory and that manager may name IPortModem.",
            ["IPortModem"],
            ModemAccessOwners),

        new ArchitectureRule(
            "A006",
            "Sessions start through the coordinator; a bulk start-all path bypasses supervision and backoff.",
            ["StartAllEnabledPortsAsync"],
            new HashSet<string>(StringComparer.Ordinal)),

        new ArchitectureRule(
            "A007",
            "Bounded queues belong to a named owner that also owns the consumer and drop policy.",
            ["Channel.CreateBounded", "Channel.CreateUnbounded"],
            BoundedQueueOwners),

        new ArchitectureRule(
            "A008",
            "Keep-alive transitions are serialized by IKeepAliveService; feature code holds a reason instead of driving the platform foreground service.",
            ["IForegroundService"],
            ForegroundServiceOwners)
    ];

    /// <summary>Scans the live production tree.</summary>
    public static IReadOnlyList<ArchitectureViolation> Scan() =>
        Scan(SourceScanner.ReadProductionSources());

    /// <summary>Scans an explicit set of sources, so rules can be unit-tested in isolation.</summary>
    public static IReadOnlyList<ArchitectureViolation> Scan(IEnumerable<ScannedSource> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        var violations = new List<ArchitectureViolation>();

        foreach (var source in sources)
        {
            var probe = source with { Text = SourceTextStripper.StripCommentsAndLiterals(source.Text) };

            foreach (var rule in Rules)
            {
                if (rule.IsAllowed(source))
                {
                    continue;
                }

                violations.AddRange(rule.FindViolations(probe)
                    .Select(symbol => new ArchitectureViolation(rule.Id, source.RelativePath, source.TypeName, symbol)));
            }
        }

        return violations;
    }
}
