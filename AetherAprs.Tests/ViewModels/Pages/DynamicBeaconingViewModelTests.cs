// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AetherAprs.Models;
using AetherAprs.Models.Aprs;
using AetherAprs.Models.Aprs.Packets;
using AetherAprs.Services.Beaconing;
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

        DynamicBeaconingViewModel vm = new(beacon, navigation);

        Assert.Equal(DynamicBeaconMode.Drive, vm.ActiveMode);
    }

    [Fact]
    public async Task Close_PersistsAllModeConfigurations_ThenNavigatesBack()
    {
        RecordingBeaconService beacon = new();
        INavigationService navigation = Substitute.For<INavigationService>();
        DynamicBeaconingViewModel vm = new(beacon, navigation);

        vm.WalkConfiguration!.SlowIntervalSeconds = 1234;
        vm.DriveConfiguration!.FastIntervalSeconds = 42;
        vm.CustomConfiguration!.BeaconComment = "Test comment";

        await ((IAsyncRelayCommand)vm.CloseCommand).ExecuteAsync(null);

        Assert.Contains(beacon.Updated, c => c.Mode == DynamicBeaconMode.Walk && c.SlowIntervalSeconds == 1234);
        Assert.Contains(beacon.Updated, c => c.Mode == DynamicBeaconMode.Drive && c.FastIntervalSeconds == 42);
        Assert.Contains(beacon.Updated, c => c.Mode == DynamicBeaconMode.Custom && c.BeaconComment == "Test comment");
        navigation.Received(1).GoBack();
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
