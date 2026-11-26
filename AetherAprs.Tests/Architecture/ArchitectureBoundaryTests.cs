// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace AetherAprs.Tests.Architecture;

public sealed class ArchitectureBoundaryTests
{
    [Fact]
    public void ProductionSource_IntroducesNoUnbaselinedBoundaryViolation()
    {
        var violations = ArchitectureBoundaryScanner.Scan();
        var unbaselined = violations
            .Where(violation => !ArchitectureBaseline.Entries.ContainsKey(violation.Key))
            .Select(violation => violation.ToString())
            .OrderBy(text => text, StringComparer.Ordinal)
            .ToArray();

        Assert.True(unbaselined.Length == 0,
            "New architecture boundary violations were found. Fix them or, if this is a" + Environment.NewLine +
            "newly discovered pre-existing violation, add it to ArchitectureBaseline with an" + Environment.NewLine +
            "owning roadmap work item:" + Environment.NewLine + string.Join(Environment.NewLine, unbaselined));
    }

    [Fact]
    public void Baseline_ContainsNoStaleEntry()
    {
        var found = ArchitectureBoundaryScanner.Scan().Select(violation => violation.Key).ToHashSet(StringComparer.Ordinal);
        var stale = ArchitectureBaseline.Entries.Keys
            .Where(key => !found.Contains(key))
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToArray();

        Assert.True(stale.Length == 0,
            "These baselined violations no longer exist. Delete the baseline entry in the same" + Environment.NewLine +
            "change that removed the violation:" + Environment.NewLine + string.Join(Environment.NewLine, stale));
    }

    [Fact]
    public void ProductionSource_ContainsNoDebugWriteLine()
    {
        Assert.Empty(ArchitectureBoundaryScanner.Scan()
            .Where(violation => violation.RuleId == "A003")
            .Select(violation => violation.ToString()));
    }

    public static TheoryData<string, string> PersistenceViolations => new()
    {
        { "AetherAprs/Services/Ports/PortService.cs", "IDbContextFactory<AppDbContext> factory = null!;" },
        { "AetherAprs/Services/Messaging/MessageService.cs", "await db.SaveChangesAsync(token);" },
        { "AetherAprs/ViewModels/Pages/MessagesViewModel.cs", "var set = context.Set<PacketRecord>();" }
    };

    [Theory]
    [MemberData(nameof(PersistenceViolations))]
    public void Scan_SyntheticPersistenceInFeatureCode_IsRejected(string path, string offendingLine)
    {
        var violations = ArchitectureBoundaryScanner.Scan([Source(path, offendingLine)]);

        Assert.Contains(violations, violation => violation.RuleId == "A001");
    }

    [Fact]
    public void Scan_SyntheticUiDispatchInFeatureCode_IsRejected()
    {
        var violations = ArchitectureBoundaryScanner.Scan(
            [Source("AetherAprs/Services/Messaging/MessageService.cs", "Dispatcher.UIThread.Post(action);")]);

        Assert.Contains(violations, violation => violation.RuleId == "A002");
    }

    [Fact]
    public void Scan_SyntheticDebugWriteLine_IsRejected()
    {
        var violations = ArchitectureBoundaryScanner.Scan(
            [Source("AetherAprs/Services/Ports/PortService.cs", "Debug.WriteLine(\"x\");")]);

        Assert.Contains(violations, violation => violation.RuleId == "A003");
    }

    [Fact]
    public void Scan_SyntheticModemConstructionInSessionCode_IsRejected()
    {
        var violations = ArchitectureBoundaryScanner.Scan(
            [
                Source("AetherAprs/Services/Ports/PortService.cs",
                    "var modem = new KissModem(stream, loggerFactory);"),
                Source("AetherAprs/Services/Ports/PortService.cs",
                    "var modem = new AprsIsModem(host, port, callsign, passcode);")
            ]);

        Assert.Equal(2, violations.Count(violation => violation.RuleId == "A004"));
    }

    [Fact]
    public void Scan_ModemFactory_MayConstructModems()
    {
        var violations = ArchitectureBoundaryScanner.Scan(
            [Source("AetherAprs/Services/Ports/ModemFactory.cs", "var modem = new AfskModem(a, b, c, settings);")]);

        Assert.Empty(violations);
    }

    [Fact]
    public void Scan_SyntheticModemHandleInFeatureCode_IsRejected()
    {
        var violations = ArchitectureBoundaryScanner.Scan(
        [
            Source(
                "AetherAprs/Services/Messaging/MessageService.cs",
                "private readonly IPortModem _modem;"),
            Source(
                "AetherAprs/Services/Ports/PortService.cs",
                "public IPortModem Modem { get; init; }")
        ]);

        Assert.Equal(2, violations.Count);
        Assert.All(violations, violation => Assert.Equal("A005", violation.RuleId));
    }

    [Fact]
    public void Scan_ModemHandleInTheSessionManager_IsAllowed()
    {
        var violations = ArchitectureBoundaryScanner.Scan(
            [Source("AetherAprs/Services/Ports/PortSessionManager.cs", "public required IPortModem Modem { get; init; }")]);

        Assert.Empty(violations);
    }

    [Fact]
    public void Scan_SyntheticStartAllPath_IsRejected()
    {
        var violations = ArchitectureBoundaryScanner.Scan(
            [Source("AetherAprs/Services/Ports/PortService.cs", "await StartAllEnabledPortsAsync();")]);

        var violation = Assert.Single(violations);
        Assert.Equal("A006", violation.RuleId);
        Assert.Equal("StartAllEnabledPortsAsync", violation.Symbol);
    }

    [Fact]
    public void Scan_SyntheticQueueInFeatureCode_IsRejected()
    {
        var violations = ArchitectureBoundaryScanner.Scan(
            [Source("AetherAprs/Services/Ports/PortService.cs",
                "var queue = Channel.CreateBounded<AprsPacket>(1048576);")]);

        var violation = Assert.Single(violations);
        Assert.Equal("A007", violation.RuleId);
        Assert.Equal("Channel.CreateBounded", violation.Symbol);
    }

    [Fact]
    public void Scan_PipelineOwnedQueue_IsAllowed()
    {
        var violations = ArchitectureBoundaryScanner.Scan(
            [Source("AetherAprs/Services/Packets/PacketIngestPipeline.cs",
                "var queue = Channel.CreateBounded<AprsPacket>(1024);")]);

        Assert.Empty(violations);
    }

    [Fact]
    public void Scan_SyntheticForegroundServiceInFeatureCode_IsRejected()
    {
        var violations = ArchitectureBoundaryScanner.Scan(
        [
            Source(
                "AetherAprs/Services/Location/LocationTrackingService.cs",
                "private readonly IForegroundService _foregroundService;"),
            Source(
                "AetherAprs/Services/Ports/PortService.cs",
                "public IForegroundService ForegroundService { get; init; }")
        ]);

        Assert.Equal(2, violations.Count);
        Assert.All(violations, violation => Assert.Equal("A008", violation.RuleId));
    }

    [Fact]
    public void Scan_KeepAliveService_MayDriveTheForegroundService()
    {
        var violations = ArchitectureBoundaryScanner.Scan(
            [Source("AetherAprs/Services/Platform/KeepAliveService.cs",
                "await _foregroundService.StartAsync(requiredTypes);")]);

        Assert.Empty(violations);
    }

    [Fact]
    public void Scan_SyntheticSettingsDocumentInFeatureCode_IsRejected()
    {
        var violations = ArchitectureBoundaryScanner.Scan(
        [
            Source(
                "AetherAprs/Services/Messaging/MessageTemplateService.cs",
                "private readonly AppSettingsDocument _document;"),
            Source(
                "AetherAprs/ViewModels/Pages/SettingsViewModel.cs",
                "AprsSettingsDocument document = new();")
        ]);

        Assert.Equal(2, violations.Count);
        Assert.All(violations, violation => Assert.Equal("A009", violation.RuleId));
    }

    [Fact]
    public void Scan_ConfigurationAdapter_MayBindThroughSettingsDocuments()
    {
        var violations = ArchitectureBoundaryScanner.Scan(
        [
            Source(
                "AetherAprs/Services/Configuration/ConfigurationService.cs",
                "var document = new AppSettingsDocument();"),
            Source(
                "AetherAprs/Configuration/Settings/Documents/AppSettingsDocument.cs",
                "public AprsSettingsDocument Aprs { get; set; } = new();")
        ]);

        Assert.Empty(violations);
    }

    [Fact]
    public void Scan_SyntheticStartupStepInFeatureCode_IsRejected()
    {
        var violations = ArchitectureBoundaryScanner.Scan(
        [
            Source(
                "AetherAprs/Services/Ports/PortService.cs",
                "var step = new StartupStep(\"sessions\", token => StartAsync(token));"),
            Source(
                "AetherAprs/ViewModels/Pages/HomeViewModel.cs",
                "var step = new StartupStep(\"location\", token => TrackAsync(token));")
        ]);

        Assert.Equal(2, violations.Count);
        Assert.All(violations, violation => Assert.Equal("A010", violation.RuleId));
    }

    [Fact]
    public void Scan_StartupFactory_MayDeclareTheSequence()
    {
        var violations = ArchitectureBoundaryScanner.Scan(
        [
            Source(
                "AetherAprs/Factories/StartupStepFactory.cs",
                "var step = new StartupStep(\"database\", _ => Task.CompletedTask);")
        ]);

        Assert.Empty(violations);
    }

    [Fact]
    public void Scan_SyntheticContainerInFeatureCode_IsRejected()
    {
        var violations = ArchitectureBoundaryScanner.Scan(
        [
            Source(
                "AetherAprs/Services/Ports/PortService.cs",
                "var provider = services.BuildServiceProvider();")
        ]);

        var violation = Assert.Single(violations);
        Assert.Equal("A011", violation.RuleId);
        Assert.Equal("BuildServiceProvider", violation.Symbol);
    }

    [Fact]
    public void Scan_CompositionRootMayBuildTheProvider()
    {
        var violations = ArchitectureBoundaryScanner.Scan(
        [
            Source(
                "AetherAprs/Factories/ServiceProviderFactory.cs",
                "var provider = services.BuildServiceProvider(options);"),
            Source(
                "AetherAprs/DesignData.cs",
                "var provider = services.BuildServiceProvider(options);")
        ]);

        Assert.Empty(violations);
    }

    [Fact]
    public void Scan_SyntheticStaticServiceLookup_IsRejected()
    {
        var violations = ArchitectureBoundaryScanner.Scan(
        [
            Source(
                "AetherAprs/ViewModels/Pages/HomeViewModel.cs",
                "var ports = App.GetService<IPortService>();"),
            Source(
                "AetherAprs/Services/Ports/PortService.cs",
                "var provider = ServiceProviderFactory.ServiceProvider;")
        ]);

        Assert.Equal(2, violations.Count);
        Assert.All(violations, violation => Assert.Equal("A012", violation.RuleId));
    }

    [Fact]
    public void Scan_PlatformEntryPointResolvingFromItsOwnProvider_IsAllowed()
    {
        var violations = ArchitectureBoundaryScanner.Scan(
            [Source(
                "AetherAprs.Android/MainActivity.cs",
                "var control = app.ServiceProvider.GetService(typeof(IApplicationSessionControl));")]);

        Assert.Empty(violations);
    }

    [Fact]
    public void Scan_SyntheticLogcatCallOutsideTheProvider_IsRejected()
    {
        var violations = ArchitectureBoundaryScanner.Scan(
        [
            Source(
                "AetherAprs.Android/MainActivity.cs",
                "global::Android.Util.Log.Warn(\"AetherAprs\", \"denied\");"),
            Source(
                "AetherAprs.Android/Services/Platform/AndroidPermissionService.cs",
                "global::Android.Util.Log.Info(\"AetherAprs\", \"granted\");")
        ]);

        Assert.Equal(2, violations.Count);
        Assert.All(violations, violation => Assert.Equal("A013", violation.RuleId));
    }

    [Fact]
    public void Scan_LogcatProvider_IsAllowedToNameThePlatformLog()
    {
        var violations = ArchitectureBoundaryScanner.Scan(
            [Source(
                "AetherAprs.Android/Services/Platform/AndroidLogcatLoggerProvider.cs",
                "global::Android.Util.Log.Warn(Tag, message);")]);

        Assert.Empty(violations);
    }

    [Fact]
    public void Scan_SyntheticAndroidComponentWithoutExported_IsRejected()
    {
        var violations = ArchitectureBoundaryScanner.ScanAndroidIntentSurface(
        [
            Source(
                "AetherAprs.Android/MainActivity.cs",
                "[Activity(Label = \"AetherAprs\", MainLauncher = true)]\npublic class MainActivity : AvaloniaMainActivity { }"),
            Source(
                "AetherAprs.Android/Services/Platform/AndroidForegroundService.cs",
                "[Service(ForegroundServiceType = ForegroundService.TypeConnectedDevice, Exported = false)]")
        ]);

        var violation = Assert.Single(violations);
        Assert.Equal("A014", violation.RuleId);
        Assert.Equal("[Activity(", violation.Symbol);
    }

    [Fact]
    public void Scan_SyntheticPendingIntentWithoutImmutable_IsRejected()
    {
        var violations = ArchitectureBoundaryScanner.ScanAndroidIntentSurface(
            [Source(
                "AetherAprs.Android/Services/Platform/AndroidNotificationService.cs",
                "var intent = PendingIntent.GetActivity(context, 0, contentIntent, PendingIntentFlags.UpdateCurrent);")]);

        var violation = Assert.Single(violations);
        Assert.Equal("A015", violation.RuleId);
        Assert.Equal("PendingIntent.GetActivity", violation.Symbol);
    }

    [Fact]
    public void Scan_SyntheticUnboundedWakeLock_IsRejected()
    {
        var violations = ArchitectureBoundaryScanner.ScanAndroidIntentSurface(
            [Source(
                "AetherAprs.Android/Services/Platform/AndroidForegroundService.cs",
                "var wakeLock = powerManager.NewWakeLock(WakeLockFlags.Partial, Tag);\nwakeLock.Acquire();")]);

        var violation = Assert.Single(violations);
        Assert.Equal("A016", violation.RuleId);
        Assert.Equal(".Acquire()", violation.Symbol);
    }

    [Fact]
    public void Scan_AndroidIntentSurface_DeclaresExportsAndImmutablePendingIntents()
    {
        var violations = ArchitectureBoundaryScanner.ScanAndroidIntentSurface(SourceScanner.ReadProductionSources());

        Assert.Empty(violations);
    }

    [Fact]
    public void Scan_SyntheticBluetoothScannerResolvingAPosition_IsRejected()
    {
        var violations = ArchitectureBoundaryScanner.ScanAndroidBluetoothLocationUse(
            [Source(
                "AetherAprs.Android/Services/Bluetooth/BlePositionResolver.cs",
                "var manager = (LocationManager)context.GetSystemService(LocationService)!;\nmanager.GetLastKnownLocation(provider);")]);

        var violation = Assert.Single(violations);
        Assert.Equal("A017", violation.RuleId);
        Assert.Equal("LocationManager", violation.Symbol);
    }

    [Fact]
    public void Scan_AndroidBluetoothAdapterDoesNotDeriveLocation()
    {
        var violations =
            ArchitectureBoundaryScanner.ScanAndroidBluetoothLocationUse(SourceScanner.ReadProductionSources());

        Assert.Empty(violations);
    }

    [Fact]
    public void ScanAndroidManifest_WhenTheScanPermissionDropsTheClaim_IsRejected()
    {
        const string manifest = """
                                <manifest xmlns:android="http://schemas.android.com/apk/res/android">
                                	<uses-permission android:name="android.permission.BLUETOOTH_SCAN" />
                                </manifest>
                                """;

        var violation = Assert.Single(ArchitectureBoundaryScanner.ScanAndroidManifest(manifest));
        Assert.Equal("A018", violation.RuleId);
        Assert.Equal("android.permission.BLUETOOTH_SCAN", violation.Symbol);
    }

    [Fact]
    public void ScanAndroidManifest_WhenTheScanPermissionIsMissing_IsRejected()
    {
        const string manifest = """
                                <manifest xmlns:android="http://schemas.android.com/apk/res/android">
                                	<uses-permission android:name="android.permission.BLUETOOTH_CONNECT" />
                                </manifest>
                                """;

        var violation = Assert.Single(ArchitectureBoundaryScanner.ScanAndroidManifest(manifest));
        Assert.Equal("A018", violation.RuleId);
    }

    [Fact]
    public void ScanAndroidManifest_DeclaresNeverForLocation()
    {
        var path = Path.Combine(
            SourceScanner.FindRepositoryRoot(),
            "AetherAprs.Android",
            "Properties",
            "AndroidManifest.xml");

        var violations = ArchitectureBoundaryScanner.ScanAndroidManifest(File.ReadAllText(path));

        Assert.Empty(violations);
    }

    [Fact]
    public void Scan_ApprovedOwnerFile_IsNotReported()
    {
        var violations = ArchitectureBoundaryScanner.Scan(
            [Source("AetherAprs/Data/Packets/PacketStorageService.cs", "await context.SaveChangesAsync(token);")]);

        Assert.Empty(violations);
    }

    [Fact]
    public void Scan_SymbolOnlyInCommentOrString_IsNotReported()
    {
        var violations = ArchitectureBoundaryScanner.Scan(
            [
                Source("AetherAprs/Services/Messaging/MessageService.cs",
                    """
                    // This used to call SaveChanges() through AppDbContext.
                    var message = "AppDbContext SaveChanges Dispatcher.UIThread";
                    /* IDbContextFactory is not used here either. */
                    """)
            ]);

        Assert.Empty(violations);
    }

    [Fact]
    public void Scan_AndroidProjectIsIncluded()
    {
        var sources = SourceScanner.ReadProductionSources();
        Assert.Contains(sources, source => source.Project == "AetherAprs.Android");

        var violations = ArchitectureBoundaryScanner.Scan(
            [Source("AetherAprs.Android/MainActivity.cs", "var factory = provider.GetService<IDbContextFactory<AppDbContext>>();")]);

        Assert.Contains(violations, violation => violation.RuleId == "A001");
    }

    [Fact]
    public void Rules_EachDeclareIdDescriptionAndSymbols()
    {
        Assert.NotEmpty(ArchitectureBoundaryScanner.Rules);
        foreach (var rule in ArchitectureBoundaryScanner.Rules)
        {
            Assert.False(string.IsNullOrWhiteSpace(rule.Id));
            Assert.False(string.IsNullOrWhiteSpace(rule.Description));
        }

        Assert.Equal(
            ArchitectureBoundaryScanner.Rules.Select(rule => rule.Id).Distinct(StringComparer.Ordinal).Count(),
            ArchitectureBoundaryScanner.Rules.Count);
    }

    private static ScannedSource Source(string path, string body) =>
        new(path, "namespace Example;" + Environment.NewLine + body);
}
