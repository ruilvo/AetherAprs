// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Collections.Generic;
using AetherAprs.Models;
using AetherAprs.Models.Aprs;
using AetherAprs.Services;
using AetherAprs.ViewModels.Pages;
using NSubstitute;
using Xunit;

namespace AetherAprs.Tests.ViewModels;

public sealed class DynamicBeaconingViewModelTests
{
    [Fact]
    public void Constructor_LoadsActiveModeFromService()
    {
        var beacon = new RecordingBeaconService();
        beacon.SetActiveMode(DynamicBeaconMode.Drive);
        var navigation = Substitute.For<INavigationService>();

        var vm = new DynamicBeaconingViewModel(beacon, navigation);

        Assert.Equal(DynamicBeaconMode.Drive, vm.ActiveMode);
    }

    [Fact]
    public void Close_PersistsAllModeConfigurations_ThenNavigatesBack()
    {
        var beacon = new RecordingBeaconService();
        var navigation = Substitute.For<INavigationService>();
        var vm = new DynamicBeaconingViewModel(beacon, navigation);

        vm.WalkConfiguration!.SlowIntervalSeconds = 1234;
        vm.DriveConfiguration!.FastIntervalSeconds = 42;
        vm.CustomConfiguration!.BeaconComment = "Test comment";

        vm.CloseCommand.Execute(null);

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

        public List<BeaconConfig> Updated { get; } = [];

        public BeaconConfig CurrentConfiguration => _mode switch
        {
            DynamicBeaconMode.Drive => _drive,
            DynamicBeaconMode.Custom => _custom,
            _ => _walk
        };

        public IReadOnlyList<BeaconConfig> AllConfigurations => [_walk, _drive, _custom];

        public void SetActiveMode(DynamicBeaconMode mode) => _mode = mode;

        public void UpdateConfiguration(BeaconConfig configuration)
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
        }

        public BeaconTransmitDecision EvaluateLocationUpdate(LocationData currentLocation, LocationData? previousLocation) =>
            new() { ShouldTransmit = false };

        public void ResetTransmissionTimer()
        {
        }

        public PositionPacket CreatePositionPacket(
            LocationData location,
            string callsign,
            string symbolTableCharacter = "/",
            string symbolCodeCharacter = "[") =>
            new()
            {
                Source = new Callsign("N0CALL"),
                Destination = new Callsign("APRS"),
                Latitude = location.Latitude,
                Longitude = location.Longitude
            };
    }
}
