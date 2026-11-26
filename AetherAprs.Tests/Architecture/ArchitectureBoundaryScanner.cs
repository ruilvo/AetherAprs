// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Linq;

namespace AetherAprs.Tests.Architecture;

/// <summary>
/// Enforces the persistence, resource ownership, and presentation boundaries
/// described in the architecture roadmap (work items R0.2 through R3).
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
        "AetherAprs/Services/Bluetooth/BluetoothAdvertisementBuffer.cs",
        "AetherAprs/Data/SqliteRuntimeWriter.cs",
        "AetherAprs/Services/Packets/PacketIngestPipeline.cs",
        "AetherAprs/Services/Ports/DigipeatRouter.cs",
        "AetherAprs/Services/Ports/SessionStateWriter.cs",
        "AetherAprs/Services/Ports/SessionTelemetryIntake.cs",
        "AetherAprs/Services/Transmission/BeaconTransmitQueue.cs",
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

    /// <summary>
    /// Files permitted to name a settings document. Binding documents exist only so
    /// the configuration provider can assign values key by key; every other caller
    /// reads the immutable snapshot instead.
    /// </summary>
    private static readonly HashSet<string> SettingsDocumentOwners = new(StringComparer.Ordinal)
    {
        "AetherAprs/Configuration/Settings/Documents/AppSettingsDocument.cs",
        "AetherAprs/Configuration/Settings/Documents/AprsSettingsDocument.cs",
        "AetherAprs/Configuration/Settings/Documents/MapSettingsDocument.cs",
        "AetherAprs/Configuration/Settings/Documents/MessageTemplatesSettingsDocument.cs",
        "AetherAprs/Configuration/Settings/Documents/MessageTemplateDocument.cs",
        "AetherAprs/Services/Configuration/ConfigurationService.cs"
    };

    /// <summary>
    /// Files permitted to declare a startup step. The sequence is a composition-root
    /// concern: a feature that needs work at startup asks the factory for a step, so
    /// the order stays in one reviewable place.
    /// </summary>
    private static readonly HashSet<string> StartupSequenceOwners = new(StringComparer.Ordinal)
    {
        "AetherAprs/Factories/StartupStepFactory.cs"
    };

    /// <summary>
    /// Files permitted to build a service provider. Only the composition root owns
    /// containers; a second container would hide registrations from validation.
    /// </summary>
    private static readonly HashSet<string> ContainerOwners = new(StringComparer.Ordinal)
    {
        "AetherAprs/Factories/ServiceProviderFactory.cs",
        "AetherAprs/DesignData.cs"
    };

    /// <summary>
    /// Files permitted to write to logcat directly. Android entry points log
    /// through <c>ILogger</c>; the logcat provider is the single adapter that
    /// translates those records for the platform before the container exists.
    /// </summary>
    private static readonly HashSet<string> AndroidLogcatOwners = new(StringComparer.Ordinal)
    {
        "AetherAprs.Android/Services/Platform/AndroidLogcatLoggerProvider.cs"
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
            ForegroundServiceOwners),

        new ArchitectureRule(
            "A009",
            "Settings documents exist only while the configuration adapter binds values; every other caller reads the immutable snapshot.",
            [
                "AppSettingsDocument", "AprsSettingsDocument", "MapSettingsDocument",
                "MessageTemplatesSettingsDocument", "MessageTemplateDocument"
            ],
            SettingsDocumentOwners),

        new ArchitectureRule(
            "A010",
            "Startup steps are declared once, in the composition root, so the startup order stays reviewable.",
            ["new StartupStep("],
            StartupSequenceOwners),

        new ArchitectureRule(
            "A011",
            "Only the composition root builds a service provider, so every registration is validated in one place.",
            ["BuildServiceProvider"],
            ContainerOwners),

        new ArchitectureRule(
            "A012",
            "Dependencies arrive through constructor injection; a static service accessor hides them from validation and from the caller.",
            ["App.GetService", "ServiceProviderFactory.ServiceProvider"],
            new HashSet<string>(StringComparer.Ordinal)),

        new ArchitectureRule(
            "A013",
            "Android code logs through ILogger; the logcat provider is the only place that names Android.Util.Log.",
            ["Android.Util.Log"],
            AndroidLogcatOwners)
    ];

    /// <summary>Scans the live production tree.</summary>
    public static IReadOnlyList<ArchitectureViolation> Scan() =>
        Scan(SourceScanner.ReadProductionSources());

    /// <summary>
    /// Android components the platform can start on behalf of another application.
    /// </summary>
    private static readonly string[] AndroidComponentAttributes = ["Activity", "Service", "Receiver"];

    private const string PendingIntentSymbol = "PendingIntent.Get";

    /// <summary>
    /// A wake lock acquisition without a timeout. The platform counts the lease as
    /// held until the process releases it, so a service that dies holding one keeps
    /// the CPU awake.
    /// </summary>
    private const string UnboundedWakeLockAcquire = ".Acquire()";

    /// <summary>
    /// The Bluetooth adapter folder. Scan results there are used to address a TNC,
    /// never to work out where the device is, which is what the manifest claims.
    /// </summary>
    private const string AndroidBluetoothPath = "AetherAprs.Android/Services/Bluetooth/";

    /// <summary>
    /// Permission element in the Android manifest that carries the scan claim.
    /// </summary>
    private const string BluetoothScanPermission = "android.permission.BLUETOOTH_SCAN";

    /// <summary>
    /// Scans the Android entry points for declared exports, immutable pending
    /// intents and unbounded wake locks.
    /// </summary>
    /// <remarks>
    /// These are text rules rather than symbol rules because the defect is a missing
    /// argument: a component that omits <c>Exported</c> still compiles, a pending
    /// intent that omits <c>Immutable</c> still works until another application
    /// rewrites its extras, and a wake lock without a timeout still compiles and
    /// passes review while draining the battery.
    /// </remarks>
    public static IReadOnlyList<ArchitectureViolation> ScanAndroidIntentSurface(IEnumerable<ScannedSource> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        var violations = new List<ArchitectureViolation>();

        foreach (var source in sources)
        {
            if (!string.Equals(source.Project, "AetherAprs.Android", StringComparison.Ordinal))
            {
                continue;
            }

            var text = SourceTextStripper.StripCommentsAndLiterals(source.Text);

            foreach (var attribute in AndroidComponentAttributes)
            {
                var anchor = $"[{attribute}(";
                var index = text.IndexOf(anchor, StringComparison.Ordinal);
                while (index >= 0)
                {
                    var end = FindClosingParenthesis(text, index + anchor.Length - 1);
                    var declaration = end < 0 ? text[index..] : text[index..end];
                    if (!declaration.Contains("Exported", StringComparison.Ordinal))
                    {
                        violations.Add(new ArchitectureViolation(
                            "A014", source.RelativePath, source.TypeName, anchor));
                    }

                    index = end < 0 ? -1 : text.IndexOf(anchor, end, StringComparison.Ordinal);
                }
            }

            var pendingIndex = text.IndexOf(PendingIntentSymbol, StringComparison.Ordinal);
            while (pendingIndex >= 0)
            {
                var statementEnd = text.IndexOf(';', pendingIndex);
                var call = statementEnd < 0 ? text[pendingIndex..] : text[pendingIndex..statementEnd];
                if (!call.Contains("Immutable", StringComparison.Ordinal))
                {
                    var nameEnd = call.IndexOf('(', StringComparison.Ordinal);
                    violations.Add(new ArchitectureViolation(
                        "A015",
                        source.RelativePath,
                        source.TypeName,
                        nameEnd < 0 ? PendingIntentSymbol : call[..nameEnd]));
                }

                pendingIndex = statementEnd < 0
                    ? -1
                    : text.IndexOf(PendingIntentSymbol, statementEnd, StringComparison.Ordinal);
            }

            var acquireIndex = text.IndexOf(UnboundedWakeLockAcquire, StringComparison.Ordinal);
            while (acquireIndex >= 0)
            {
                violations.Add(new ArchitectureViolation(
                    "A016", source.RelativePath, source.TypeName, UnboundedWakeLockAcquire));

                acquireIndex = text.IndexOf(
                    UnboundedWakeLockAcquire,
                    acquireIndex + UnboundedWakeLockAcquire.Length,
                    StringComparison.Ordinal);
            }
        }

        return violations;
    }

    /// <summary>
    /// Symbols that would let a Bluetooth advertisement become a position.
    /// </summary>
    private static readonly string[] LocationFromScanSymbols = ["Android.Locations", "LocationManager"];

    /// <summary>
    /// Scans the Android Bluetooth adapter for location APIs.
    /// </summary>
    /// <remarks>
    /// The manifest declares <c>neverForLocation</c> on the Bluetooth scan
    /// permission, which lets the application scan without the location
    /// permission. That claim holds only while nothing in the adapter resolves a
    /// position from an advertisement, so the claim and the code behind it are
    /// asserted together instead of being recorded in a document alone.
    /// </remarks>
    public static IReadOnlyList<ArchitectureViolation> ScanAndroidBluetoothLocationUse(
        IEnumerable<ScannedSource> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        var violations = new List<ArchitectureViolation>();

        foreach (var source in sources)
        {
            if (!source.RelativePath.StartsWith(AndroidBluetoothPath, StringComparison.Ordinal))
            {
                continue;
            }

            var text = SourceTextStripper.StripCommentsAndLiterals(source.Text);
            foreach (var symbol in LocationFromScanSymbols)
            {
                if (text.Contains(symbol, StringComparison.Ordinal))
                {
                    violations.Add(new ArchitectureViolation(
                        "A017", source.RelativePath, source.TypeName, symbol));
                }
            }
        }

        return violations;
    }

    /// <summary>
    /// Scans the Android manifest for the Bluetooth scan claim.
    /// </summary>
    /// <remarks>
    /// A manifest is not compiled code, so the claim can disappear in a merge
    /// without a test failing. The permission must be declared and every
    /// declaration must carry <c>neverForLocation</c>.
    /// </remarks>
    public static IReadOnlyList<ArchitectureViolation> ScanAndroidManifest(
        string manifestText,
        string relativePath = "AetherAprs.Android/Properties/AndroidManifest.xml")
    {
        ArgumentNullException.ThrowIfNull(manifestText);
        var violations = new List<ArchitectureViolation>();
        var declared = 0;

        var index = manifestText.IndexOf(BluetoothScanPermission, StringComparison.Ordinal);
        while (index >= 0)
        {
            declared++;
            var end = manifestText.IndexOf('>', index);
            var element = end < 0 ? manifestText[index..] : manifestText[index..end];
            if (!element.Contains("neverForLocation", StringComparison.Ordinal))
            {
                violations.Add(new ArchitectureViolation(
                    "A018", relativePath, "AndroidManifest", BluetoothScanPermission));
            }

            index = end < 0
                ? -1
                : manifestText.IndexOf(BluetoothScanPermission, end, StringComparison.Ordinal);
        }

        if (declared == 0)
        {
            violations.Add(new ArchitectureViolation(
                "A018", relativePath, "AndroidManifest", BluetoothScanPermission));
        }

        return violations;
    }

    private static int FindClosingParenthesis(string text, int openIndex)
    {
        var depth = 0;
        for (var index = openIndex; index < text.Length; index++)
        {
            if (text[index] == '(')
            {
                depth++;
            }
            else if (text[index] == ')' && --depth == 0)
            {
                return index;
            }
        }

        return -1;
    }

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
