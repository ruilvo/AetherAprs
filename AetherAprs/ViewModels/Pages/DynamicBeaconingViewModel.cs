// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using AetherAprs.Factories.Packets;
using AetherAprs.Models;
using AetherAprs.Models.Aprs.Packets;
using AetherAprs.Modems.Aprs;
using AetherAprs.Services.Beaconing;
using AetherAprs.Services.Configuration;
using AetherAprs.Services.Ports;
using AetherAprs.Services.Transmission;
using AetherAprs.Services.UI;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AetherAprs.ViewModels.Pages;

/// <summary>
/// ViewModel for managing dynamic beaconing configuration.
/// </summary>
public partial class DynamicBeaconingViewModel : ViewModelBase
{
    private readonly IBeaconService _beaconService;
    private readonly INavigationService _navigationService;
    private readonly IConfigurationService _configurationService;
    private readonly IAprsPortSettingsResolver _portSettingsResolver;
    private readonly IPacketFactory _packetFactory;
    private readonly IBeaconTransmitQueue _beaconQueue;

    /// <summary>
    /// A fixed sample position used when the page has no live fix to preview. The
    /// page is a configuration form, not the map, so it does not subscribe to the
    /// location worker; the encoder output is otherwise identical to a real beacon.
    /// </summary>
    private static readonly LocationData SamplePreviewLocation = new()
    {
        Location = new Geo.Coordinate(38.7223, -9.1393),
        Altitude = 100,
        Timestamp = DateTimeOffset.UnixEpoch
    };

    [ObservableProperty]
    public partial DynamicBeaconMode ActiveMode { get; set; } = DynamicBeaconMode.Walk;

    [ObservableProperty]
    public partial BeaconConfigurationItemViewModel? WalkConfiguration { get; set; }

    [ObservableProperty]
    public partial BeaconConfigurationItemViewModel? DriveConfiguration { get; set; }

    [ObservableProperty]
    public partial BeaconConfigurationItemViewModel? CustomConfiguration { get; set; }

    /// <summary>Gets the APRS info field that the next beacon would carry.</summary>
    [ObservableProperty]
    public partial string PreviewInfoField { get; set; } = string.Empty;

    /// <summary>Gets the digipeater path the next beacon would use.</summary>
    [ObservableProperty]
    public partial string PreviewPath { get; set; } = string.Empty;

    /// <summary>Gets the symbol (table + code) the next beacon would carry.</summary>
    [ObservableProperty]
    public partial string PreviewSymbol { get; set; } = string.Empty;

    /// <summary>
    /// Gets the localized status of beacon work that was lost (expired or dropped),
    /// or an empty string when nothing was lost.
    /// </summary>
    [ObservableProperty]
    public partial string StaleBeaconStatus { get; set; } = string.Empty;

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

    public DynamicBeaconingViewModel(
        IBeaconService beaconService,
        INavigationService navigationService,
        IConfigurationService configurationService,
        IAprsPortSettingsResolver portSettingsResolver,
        IPacketFactory packetFactory,
        IBeaconTransmitQueue beaconQueue)
    {
        _beaconService = beaconService ?? throw new ArgumentNullException(nameof(beaconService));
        _navigationService = navigationService ?? throw new ArgumentNullException(nameof(navigationService));
        _configurationService = configurationService ?? throw new ArgumentNullException(nameof(configurationService));
        _portSettingsResolver = portSettingsResolver ?? throw new ArgumentNullException(nameof(portSettingsResolver));
        _packetFactory = packetFactory ?? throw new ArgumentNullException(nameof(packetFactory));
        _beaconQueue = beaconQueue ?? throw new ArgumentNullException(nameof(beaconQueue));

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

        // The preview follows the live edits: each item ViewModel is owned by this
        // page, so subscribing to it cannot keep the page alive after navigation.
        AttachConfiguration(WalkConfiguration);
        AttachConfiguration(DriveConfiguration);
        AttachConfiguration(CustomConfiguration);

        RefreshPreview();
        _ = RefreshStaleStatusAsync();
    }

    /// <summary>
    /// Builds the packet preview the next beacon would transmit, so the operator can
    /// verify the encoded position, path and symbol before going on the air.
    /// </summary>
    /// <param name="location">
    /// The position to preview. When omitted, a fixed sample position is used because
    /// this page intentionally does not own the location worker.
    /// </param>
    public void RefreshPreview(LocationData? location = null)
    {
        LocationData sample = location ?? SamplePreviewLocation;

        string callsign = _configurationService.Current.Aprs.Callsign;
        if (string.IsNullOrWhiteSpace(callsign))
        {
            // An unconfigured station still deserves an honest preview of the encoding.
            callsign = "N0CALL";
        }

        BeaconConfig configuration = ActiveConfiguration();
        PositionPacket? packet = _packetFactory.CreatePositionPacket(
            sample,
            callsign,
            _portSettingsResolver.GetSymbolTable(),
            _portSettingsResolver.GetSymbolCode(),
            course: null,
            comment: configuration.BeaconComment);
        if (packet is null)
        {
            // A factory that cannot build a packet leaves the preview blank instead of
            // failing the settings page.
            PreviewInfoField = string.Empty;
            PreviewPath = string.Empty;
            PreviewSymbol = string.Empty;
            return;
        }

        PreviewInfoField = AprsInfoFieldSerializer.FormatInfoField(packet);
        PreviewPath = packet.Path.Count == 0
            ? string.Empty
            : string.Join(",", packet.Path.Select(callsignPart => callsignPart.ToString()));
        PreviewSymbol = $"{packet.Symbol.TableChar}{packet.Symbol.CodeChar}";
    }

    /// <summary>
    /// Loads how many beacons expired or were dropped without being transmitted and
    /// exposes it as a localized status. Failures leave the status empty: this is a
    /// presentation aid and must not break the settings page.
    /// </summary>
    public async Task RefreshStaleStatusAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            int staleCount = await _beaconQueue.GetStaleCountAsync(cancellationToken).ConfigureAwait(true);
            StaleBeaconStatus = staleCount > 0
                ? Localization.Strings.Format("BeaconStaleCount", staleCount)
                : string.Empty;
        }
        catch (Exception)
        {
            StaleBeaconStatus = string.Empty;
        }
    }

    private BeaconConfig ActiveConfiguration() => ActiveMode switch
    {
        DynamicBeaconMode.Walk => WalkConfiguration?.Configuration ?? BeaconConfig.CreateWalkPreset(),
        DynamicBeaconMode.Drive => DriveConfiguration?.Configuration ?? BeaconConfig.CreateDrivePreset(),
        _ => CustomConfiguration?.Configuration ?? BeaconConfig.CreateCustomPreset()
    };

    private void AttachConfiguration(BeaconConfigurationItemViewModel? configuration)
    {
        if (configuration is null)
        {
            return;
        }

        configuration.PropertyChanged += OnConfigurationEdited;
    }

    private void OnConfigurationEdited(object? sender, PropertyChangedEventArgs e) => RefreshPreview();


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
        RefreshPreview();
    }

    partial void OnWalkConfigurationChanged(BeaconConfigurationItemViewModel? oldValue, BeaconConfigurationItemViewModel? newValue)
    {
        if (oldValue is not null)
        {
            oldValue.PropertyChanged -= OnConfigurationEdited;
        }

        AttachConfiguration(newValue);
        RefreshPreview();
    }

    partial void OnDriveConfigurationChanged(BeaconConfigurationItemViewModel? oldValue, BeaconConfigurationItemViewModel? newValue)
    {
        if (oldValue is not null)
        {
            oldValue.PropertyChanged -= OnConfigurationEdited;
        }

        AttachConfiguration(newValue);
        RefreshPreview();
    }

    partial void OnCustomConfigurationChanged(BeaconConfigurationItemViewModel? oldValue, BeaconConfigurationItemViewModel? newValue)
    {
        if (oldValue is not null)
        {
            oldValue.PropertyChanged -= OnConfigurationEdited;
        }

        AttachConfiguration(newValue);
        RefreshPreview();
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
