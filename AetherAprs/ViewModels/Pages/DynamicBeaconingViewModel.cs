// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Models;
using AetherAprs.Services.Beaconing;
using AetherAprs.Services.UI;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

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

    /// <summary>Selectable beacon modes, labelled once so the picker binds to typed items.</summary>
    public IReadOnlyList<BeaconModeChoice> ModeChoices { get; } =
    [
        new(DynamicBeaconMode.Walk, Localization.Strings.Get("WalkPedestrian")),
        new(DynamicBeaconMode.Drive, Localization.Strings.Get("DriveVehicular")),
        new(DynamicBeaconMode.Custom, Localization.Strings.Get("Custom")),
    ];

    /// <summary>
    /// The picker's selection. Setting it moves <see cref="ActiveMode"/>; persisting the
    /// new mode stays with <see cref="SetActiveModeAsync"/>.
    /// </summary>
    public BeaconModeChoice? SelectedModeChoice
    {
        get => FindChoice(ActiveMode);
        set
        {
            if (value is not null && value.Mode != ActiveMode)
            {
                ActiveMode = value.Mode;
            }
        }
    }

    public DynamicBeaconingViewModel(IBeaconService beaconService, INavigationService navigationService)
    {
        _beaconService = beaconService ?? throw new ArgumentNullException(nameof(beaconService));
        _navigationService = navigationService ?? throw new ArgumentNullException(nameof(navigationService));

        // Initialize configuration view models.
        // Documented exception to the child-ViewModel-injection rule: BeaconConfigurationItemViewModel
        // is a stateless presentation wrapper over a BeaconConfig record with no service dependencies,
        // so a factory would only delegate to `new`. See AGENTS.md "Child ViewModel Injection".
        IReadOnlyList<BeaconConfig> configs = beaconService.AllConfigurations;
        foreach (BeaconConfig config in configs)
        {
            BeaconConfigurationItemViewModel itemVm = new(config);
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
    private async Task CloseAsync(CancellationToken cancellationToken)
    {
        await SaveAllConfigurationsAsync(cancellationToken).ConfigureAwait(true);
        _navigationService.GoBack();
    }

    /// <summary>
    /// Activates <paramref name="mode"/>. The service persists the choice before it
    /// takes effect; a failed write is reported and the selection is reverted.
    /// </summary>
    public async Task SetActiveModeAsync(DynamicBeaconMode mode)
    {
        if (ActiveMode == mode)
        {
            return;
        }

        DynamicBeaconMode previous = ActiveMode;
        ActiveMode = mode;
        try
        {
            await _beaconService.SetActiveModeAsync(mode).ConfigureAwait(true);
        }
        catch (Exception)
        {
            ActiveMode = previous;
            throw;
        }
    }

    partial void OnActiveModeChanged(DynamicBeaconMode value)
    {
        // The explicit command owns persistence; a property-changed handler cannot
        // await a write or report its failure, so it must not mutate durable state.
        OnPropertyChanged(nameof(SelectedModeChoice));
    }

    private BeaconModeChoice? FindChoice(DynamicBeaconMode mode)
    {
        foreach (BeaconModeChoice choice in ModeChoices)
        {
            if (choice.Mode == mode)
            {
                return choice;
            }
        }

        return null;
    }

    [RelayCommand]
    public async Task SaveAllConfigurationsAsync(CancellationToken cancellationToken)
    {
        if (WalkConfiguration?.Configuration is { } walk)
        {
            await _beaconService.UpdateConfigurationAsync(walk, cancellationToken).ConfigureAwait(true);
        }

        if (DriveConfiguration?.Configuration is { } drive)
        {
            await _beaconService.UpdateConfigurationAsync(drive, cancellationToken).ConfigureAwait(true);
        }

        if (CustomConfiguration?.Configuration is { } custom)
        {
            await _beaconService.UpdateConfigurationAsync(custom, cancellationToken).ConfigureAwait(true);
        }
    }

    [RelayCommand]
    public void ResetWalkToDefault()
    {
        BeaconConfig defaultWalk = BeaconConfig.CreateWalkPreset();
        // Documented exception to child-ViewModel injection (stateless config wrapper, no services) — see AGENTS.md.
        WalkConfiguration = new BeaconConfigurationItemViewModel(defaultWalk);
    }

    [RelayCommand]
    public void ResetDriveToDefault()
    {
        BeaconConfig defaultDrive = BeaconConfig.CreateDrivePreset();
        // Documented exception to child-ViewModel injection (stateless config wrapper, no services) — see AGENTS.md.
        DriveConfiguration = new BeaconConfigurationItemViewModel(defaultDrive);
    }

    [RelayCommand]
    public void ResetCustomToDefault()
    {
        BeaconConfig defaultCustom = BeaconConfig.CreateCustomPreset();
        // Documented exception to child-ViewModel injection (stateless config wrapper, no services) — see AGENTS.md.
        CustomConfiguration = new BeaconConfigurationItemViewModel(defaultCustom);
    }
}

/// <summary>One selectable beacon mode with its localized label.</summary>
/// <param name="Mode">The mode the picker selects.</param>
/// <param name="DisplayName">The localized label shown for the mode.</param>
public sealed record BeaconModeChoice(DynamicBeaconMode Mode, string DisplayName);

/// <summary>
/// ViewModel for a single beacon configuration that can be edited.
/// </summary>
public partial class BeaconConfigurationItemViewModel(BeaconConfig configuration) : ViewModelBase
{
    [ObservableProperty]
    public partial BeaconConfig Configuration { get; set; } = configuration;

    [ObservableProperty]
    public partial string DisplayName { get; set; } = configuration.GetLocalizedDisplayName();

    [ObservableProperty]
    public partial int SlowIntervalSeconds { get; set; } = configuration.SlowIntervalSeconds;

    [ObservableProperty]
    public partial int NormalIntervalSeconds { get; set; } = configuration.NormalIntervalSeconds;

    [ObservableProperty]
    public partial int FastIntervalSeconds { get; set; } = configuration.FastIntervalSeconds;

    [ObservableProperty]
    public partial double FastSpeedThresholdKmh { get; set; } = configuration.FastSpeedThresholdKmh;

    [ObservableProperty]
    public partial double SlowSpeedThresholdKmh { get; set; } = configuration.SlowSpeedThresholdKmh;

    [ObservableProperty]
    public partial int CourseChangeThresholdDegrees { get; set; } = configuration.CourseChangeThresholdDegrees;

    [ObservableProperty]
    public partial int MinimumDistanceMeters { get; set; } = configuration.MinimumDistanceMeters;

    [ObservableProperty]
    public partial string BeaconComment { get; set; } = configuration.BeaconComment ?? string.Empty;

    partial void OnDisplayNameChanged(string value) => UpdateConfiguration();
    partial void OnSlowIntervalSecondsChanged(int value) => UpdateConfiguration();
    partial void OnNormalIntervalSecondsChanged(int value) => UpdateConfiguration();
    partial void OnFastIntervalSecondsChanged(int value) => UpdateConfiguration();
    partial void OnFastSpeedThresholdKmhChanged(double value) => UpdateConfiguration();
    partial void OnSlowSpeedThresholdKmhChanged(double value) => UpdateConfiguration();
    partial void OnCourseChangeThresholdDegreesChanged(int value) => UpdateConfiguration();
    partial void OnMinimumDistanceMetersChanged(int value) => UpdateConfiguration();
    partial void OnBeaconCommentChanged(string value) => UpdateConfiguration();

    private void UpdateConfiguration() => Configuration = Configuration with
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
