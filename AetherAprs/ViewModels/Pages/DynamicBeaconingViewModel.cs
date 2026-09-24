// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AetherAprs.Models;
using AetherAprs.Services;
using AetherAprs.ViewModels;

namespace AetherAprs.ViewModels.Pages;

/// <summary>
/// ViewModel for managing dynamic beaconing configuration.
/// </summary>
public partial class DynamicBeaconingViewModel : ViewModelBase
{
    private readonly IBeaconService _beaconService;
    private readonly INavigationService _navigationService;

    [ObservableProperty]
    public partial DynamicBeaconMode ActiveMode { get; set; } = DynamicBeaconMode.Walk;

    [ObservableProperty]
    public partial BeaconConfigurationItemViewModel? WalkConfiguration { get; set; }

    [ObservableProperty]
    public partial BeaconConfigurationItemViewModel? DriveConfiguration { get; set; }

    [ObservableProperty]
    public partial BeaconConfigurationItemViewModel? CustomConfiguration { get; set; }

    public DynamicBeaconingViewModel(IBeaconService beaconService, INavigationService navigationService)
    {
        _beaconService = beaconService ?? throw new ArgumentNullException(nameof(beaconService));
        _navigationService = navigationService ?? throw new ArgumentNullException(nameof(navigationService));

        // Initialize configuration view models
        var configs = beaconService.AllConfigurations;
        foreach (var config in configs)
        {
            var itemVm = new BeaconConfigurationItemViewModel(config);
            switch (config.Mode)
            {
                case DynamicBeaconMode.Walk:
                    WalkConfiguration = itemVm;
                    break;
                case DynamicBeaconMode.Drive:
                    DriveConfiguration = itemVm;
                    break;
                case DynamicBeaconMode.Custom:
                    CustomConfiguration = itemVm;
                    break;
            }
        }

        ActiveMode = beaconService.CurrentConfiguration.Mode;
    }


    [RelayCommand]
    private void Close()
    {
        SaveAllConfigurations();
        _navigationService.GoBack();
    }

    [RelayCommand]
    public void SetActiveMode(DynamicBeaconMode mode)
    {
        if (ActiveMode == mode)
            return;

        ActiveMode = mode;
    }

    partial void OnActiveModeChanged(DynamicBeaconMode value)
    {
        _beaconService.SetActiveMode(value);
    }

    [RelayCommand]
    public void SaveAllConfigurations()
    {
        if (WalkConfiguration?.Configuration is { } walk)
            _beaconService.UpdateConfiguration(walk);
        if (DriveConfiguration?.Configuration is { } drive)
            _beaconService.UpdateConfiguration(drive);
        if (CustomConfiguration?.Configuration is { } custom)
            _beaconService.UpdateConfiguration(custom);
    }

    [RelayCommand]
    public void ResetWalkToDefault()
    {
        var defaultWalk = BeaconConfig.CreateWalkPreset();
        WalkConfiguration = new BeaconConfigurationItemViewModel(defaultWalk);
    }

    [RelayCommand]
    public void ResetDriveToDefault()
    {
        var defaultDrive = BeaconConfig.CreateDrivePreset();
        DriveConfiguration = new BeaconConfigurationItemViewModel(defaultDrive);
    }

    [RelayCommand]
    public void ResetCustomToDefault()
    {
        var defaultCustom = BeaconConfig.CreateCustomPreset();
        CustomConfiguration = new BeaconConfigurationItemViewModel(defaultCustom);
    }
}

/// <summary>
/// ViewModel for a single beacon configuration that can be edited.
/// </summary>
public partial class BeaconConfigurationItemViewModel : ViewModelBase
{
    [ObservableProperty]
    public partial BeaconConfig Configuration { get; set; }

    [ObservableProperty]
    public partial string DisplayName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial int SlowIntervalSeconds { get; set; }

    [ObservableProperty]
    public partial int NormalIntervalSeconds { get; set; }

    [ObservableProperty]
    public partial int FastIntervalSeconds { get; set; }

    [ObservableProperty]
    public partial double FastSpeedThresholdKmh { get; set; }

    [ObservableProperty]
    public partial double SlowSpeedThresholdKmh { get; set; }

    [ObservableProperty]
    public partial int CourseChangeThresholdDegrees { get; set; }

    [ObservableProperty]
    public partial int MinimumDistanceMeters { get; set; }

    [ObservableProperty]
    public partial string BeaconComment { get; set; } = string.Empty;

    public BeaconConfigurationItemViewModel(BeaconConfig configuration)
    {
        Configuration = configuration;
        DisplayName = configuration.DisplayName;
        SlowIntervalSeconds = configuration.SlowIntervalSeconds;
        NormalIntervalSeconds = configuration.NormalIntervalSeconds;
        FastIntervalSeconds = configuration.FastIntervalSeconds;
        FastSpeedThresholdKmh = configuration.FastSpeedThresholdKmh;
        SlowSpeedThresholdKmh = configuration.SlowSpeedThresholdKmh;
        CourseChangeThresholdDegrees = configuration.CourseChangeThresholdDegrees;
        MinimumDistanceMeters = configuration.MinimumDistanceMeters;
        BeaconComment = configuration.BeaconComment ?? string.Empty;
    }

    partial void OnDisplayNameChanged(string value) => UpdateConfiguration();
    partial void OnSlowIntervalSecondsChanged(int value) => UpdateConfiguration();
    partial void OnNormalIntervalSecondsChanged(int value) => UpdateConfiguration();
    partial void OnFastIntervalSecondsChanged(int value) => UpdateConfiguration();
    partial void OnFastSpeedThresholdKmhChanged(double value) => UpdateConfiguration();
    partial void OnSlowSpeedThresholdKmhChanged(double value) => UpdateConfiguration();
    partial void OnCourseChangeThresholdDegreesChanged(int value) => UpdateConfiguration();
    partial void OnMinimumDistanceMetersChanged(int value) => UpdateConfiguration();
    partial void OnBeaconCommentChanged(string value) => UpdateConfiguration();

    private void UpdateConfiguration()
    {
        Configuration = Configuration with
        {
            DisplayName = DisplayName,
            SlowIntervalSeconds = SlowIntervalSeconds,
            NormalIntervalSeconds = NormalIntervalSeconds,
            FastIntervalSeconds = FastIntervalSeconds,
            FastSpeedThresholdKmh = FastSpeedThresholdKmh,
            SlowSpeedThresholdKmh = SlowSpeedThresholdKmh,
            CourseChangeThresholdDegrees = CourseChangeThresholdDegrees,
            MinimumDistanceMeters = MinimumDistanceMeters,
            BeaconComment = string.IsNullOrWhiteSpace(BeaconComment) ? null : BeaconComment
        };
    }
}
