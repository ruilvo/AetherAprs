// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AetherAprs.Configuration.Settings;
using AetherAprs.Factories.Packets;
using AetherAprs.Localization;
using AetherAprs.Models;
using AetherAprs.Models.Aprs;
using AetherAprs.Models.Aprs.Packets;
using AetherAprs.Services.Beaconing;
using AetherAprs.Services.Configuration;
using AetherAprs.Services.Ports;
using AetherAprs.Services.Transmission;
using AetherAprs.Services.UI;
using AetherAprs.ViewModels.Pages;
using CommunityToolkit.Mvvm.Input;
using Geo;
using NSubstitute;
using Xunit;

namespace AetherAprs.Tests.ViewModels.Pages;

public sealed class DynamicBeaconingViewModelTests
{
    [Fact]
    public async Task Constructor_LoadsActiveModeFromService()
    {
        RecordingBeaconService beacon = new();
        await beacon.SetActiveModeAsync(DynamicBeaconMode.Drive, TestContext.Current.CancellationToken);
        INavigationService navigation = Substitute.For<INavigationService>();

        DynamicBeaconingViewModel vm = CreateViewModel(beacon, navigation);

        Assert.Equal(DynamicBeaconMode.Drive, vm.ActiveMode);
    }

    [Fact]
    public async Task Close_PersistsAllModeConfigurations_ThenNavigatesBack()
    {
        RecordingBeaconService beacon = new();
        INavigationService navigation = Substitute.For<INavigationService>();
        DynamicBeaconingViewModel vm = CreateViewModel(beacon, navigation);

        vm.WalkConfiguration!.SlowIntervalSeconds = 1234;
        vm.DriveConfiguration!.FastIntervalSeconds = 42;
        vm.CustomConfiguration!.BeaconComment = "Test comment";

        await ((IAsyncRelayCommand)vm.CloseCommand).ExecuteAsync(null);

        Assert.Contains(beacon.Updated, c => c.Mode == DynamicBeaconMode.Walk && c.SlowIntervalSeconds == 1234);
        Assert.Contains(beacon.Updated, c => c.Mode == DynamicBeaconMode.Drive && c.FastIntervalSeconds == 42);
        Assert.Contains(beacon.Updated, c => c.Mode == DynamicBeaconMode.Custom && c.BeaconComment == "Test comment");
        navigation.Received(1).GoBack();
    }

    [Fact]
    public void RefreshPreview_EncodesThePositionPathAndSymbolThatWouldBeTransmitted()
    {
        RecordingBeaconService beacon = new();
        TestConfigurationService configuration = TestConfigurationService.Create(
            callsign: "N0CALL",
            digipeaterPath: "WIDE1-1,WIDE2-2",
            symbolTable: SymbolTable.Primary,
            symbolCode: SymbolCode.GreaterThanSign,
            format: PositionFormat.Uncompressed);
        DynamicBeaconingViewModel vm = CreateViewModel(
            beacon,
            Substitute.For<INavigationService>(),
            configuration);

        LocationData location = new()
        {
            Location = new Coordinate(38.7223, -9.1393),
            Altitude = 100,
            Timestamp = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero)
        };

        vm.RefreshPreview(location);

        // Deterministic input: the uncompressed APRS info field, the configured path
        // and the configured symbol are exactly what the encoder would put on the air.
        Assert.Equal("!3843.34N/00908.36W>Walking", vm.PreviewInfoField);
        Assert.Equal("WIDE1-1,WIDE2-2", vm.PreviewPath);
        Assert.Equal("/>", vm.PreviewSymbol);
    }

    [Fact]
    public void RefreshPreview_UsesTheActiveModesComment()
    {
        RecordingBeaconService beacon = new();
        DynamicBeaconingViewModel vm = CreateViewModel(
            beacon,
            Substitute.For<INavigationService>(),
            TestConfigurationService.Create(
                callsign: "N0CALL",
                symbolTable: SymbolTable.Primary,
                symbolCode: SymbolCode.GreaterThanSign,
                format: PositionFormat.Uncompressed));

        vm.DriveConfiguration!.BeaconComment = "Driving fast";
        vm.ActiveMode = DynamicBeaconMode.Drive;

        Assert.Contains("Driving fast", vm.PreviewInfoField, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RefreshStaleStatusAsync_SurfacesTheLostBeaconCount()
    {
        RecordingBeaconService beacon = new();
        StubBeaconQueue queue = new(staleCount: 2);
        DynamicBeaconingViewModel vm = CreateViewModel(
            beacon,
            Substitute.For<INavigationService>(),
            queue: queue);

        await vm.RefreshStaleStatusAsync(TestContext.Current.CancellationToken);

        Assert.Equal(Strings.Format("BeaconStaleCount", 2), vm.StaleBeaconStatus);
    }

    [Fact]
    public async Task RefreshStaleStatusAsync_WithNoLostWork_LeavesTheStatusEmpty()
    {
        RecordingBeaconService beacon = new();
        DynamicBeaconingViewModel vm = CreateViewModel(
            beacon,
            Substitute.For<INavigationService>(),
            queue: new StubBeaconQueue(staleCount: 0));

        await vm.RefreshStaleStatusAsync(TestContext.Current.CancellationToken);

        Assert.Empty(vm.StaleBeaconStatus);
    }

    private static DynamicBeaconingViewModel CreateViewModel(
        RecordingBeaconService beacon,
        INavigationService navigation,
        TestConfigurationService? configuration = null,
        StubBeaconQueue? queue = null)
    {
        configuration ??= TestConfigurationService.Create(
            callsign: "N0CALL",
            digipeaterPath: "WIDE1-1,WIDE2-1",
            symbolTable: SymbolTable.Primary,
            symbolCode: SymbolCode.GreaterThanSign,
            format: PositionFormat.Compressed);

        AprsPortSettingsResolver resolver = new(configuration);
        PacketFactory factory = new(configuration);

        return new DynamicBeaconingViewModel(
            beacon,
            navigation,
            configuration,
            resolver,
            factory,
            queue ?? new StubBeaconQueue(staleCount: 0));
    }

    private sealed class TestConfigurationService : IConfigurationService
    {
#pragma warning disable CS0067 // Event is never used in this test double
        public event EventHandler? SettingsChanged;
#pragma warning restore CS0067

        public AppSettings Current { get; private set; } = new();

        public Task<AppSettings> UpdateAsync(
            Func<AppSettings, AppSettings> update,
            CancellationToken cancellationToken = default)
        {
            Current = update(Current);
            return Task.FromResult(Current);
        }

        public static TestConfigurationService Create(
            string callsign,
            string digipeaterPath = "WIDE1-1,WIDE2-1",
            SymbolTable symbolTable = SymbolTable.Primary,
            SymbolCode symbolCode = SymbolCode.GreaterThanSign,
            PositionFormat format = PositionFormat.Compressed)
        {
            TestConfigurationService service = new();
            service.Current = service.Current with
            {
                Aprs = service.Current.Aprs with
                {
                    Callsign = callsign,
                    DigipeaterPath = digipeaterPath,
                    SymbolTable = symbolTable,
                    SymbolCode = symbolCode,
                    PositionFormat = format
                }
            };
            return service;
        }
    }

    private sealed class StubBeaconQueue(int staleCount) : IBeaconTransmitQueue
    {
        public Task<BeaconEnqueueResult> EnqueueAsync(
            Guid portId,
            LocationData location,
            DateTimeOffset now,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new BeaconEnqueueResult(null, null, BeaconQueueStatus.Queued));

        public Task<IReadOnlyList<BeaconQueueItem>> ReconcileAsync(
            DateTimeOffset now,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<BeaconQueueItem>>([]);

        public Task<IReadOnlyList<BeaconQueueItem>> GetSnapshotAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<BeaconQueueItem>>([]);

        public Task<int> GetStaleCountAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(staleCount);

        public Task MarkSendingAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task MarkSentAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task MarkAttemptFailedAsync(
            Guid id,
            string failureReason,
            DateTimeOffset now,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class RecordingBeaconService : IBeaconService
    {
        private DynamicBeaconMode _mode = DynamicBeaconMode.Walk;
        private BeaconConfig _walk = BeaconConfig.CreateWalkPreset();
        private BeaconConfig _drive = BeaconConfig.CreateDrivePreset();
        private BeaconConfig _custom = BeaconConfig.CreateCustomPreset();

#pragma warning disable CS0067
        public event EventHandler<BeaconRequestedEventArgs>? BeaconRequested;
#pragma warning restore CS0067

        public List<BeaconConfig> Updated { get; } = [];

        public BeaconConfig CurrentConfiguration => _mode switch
        {
            DynamicBeaconMode.Drive => _drive,
            DynamicBeaconMode.Custom => _custom,
            _ => _walk
        };

        public IReadOnlyList<BeaconConfig> AllConfigurations => [_walk, _drive, _custom];

        public double? LastCourseDegrees => null;

        public BeaconTransmitDecision? CurrentDecision => null;

        public void ReportBeaconTransmissionResult(bool success)
        {
        }

        public Task SetActiveModeAsync(DynamicBeaconMode mode, CancellationToken cancellationToken = default)
        {
            _mode = mode;
            return Task.CompletedTask;
        }

        public Task UpdateConfigurationAsync(BeaconConfig configuration, CancellationToken cancellationToken = default)
        {
            Updated.Add(configuration);
            switch (configuration.Mode)
            {
                case DynamicBeaconMode.Walk:
                    _walk = configuration;
                    break;
                case DynamicBeaconMode.Drive:
                    _drive = configuration;
                    break;
                case DynamicBeaconMode.Custom:
                    _custom = configuration;
                    break;
            }

            return Task.CompletedTask;
        }

        public void ProcessLocationUpdate(LocationData currentLocation)
        {
        }

        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
