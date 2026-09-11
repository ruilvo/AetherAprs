// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AetherAprs.Configuration;
using AetherAprs.Models;
using AetherAprs.Services;
using AetherAprs.ViewModels.Pages;

namespace AetherAprs.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private readonly IConfigurationService _configurationService;
    private readonly INavigationService _navigationService;

    [ObservableProperty]
    public partial string Title { get; set; } = "Settings";

    [ObservableProperty]
    public partial string Description { get; set; } = "Configure your AetherAprs preferences";

    [ObservableProperty]
    public partial string Callsign { get; set; } = "N0CALL";

    [ObservableProperty]
    public partial int DefaultSsid { get; set; }

    [ObservableProperty]
    public partial string DefaultSymbolTableCharacter { get; set; } = "/";

    [ObservableProperty]
    public partial string DefaultSymbolCodeCharacter { get; set; } = "[";

    [ObservableProperty]
    public partial DynamicBeaconMode DefaultBeaconMode { get; set; } = DynamicBeaconMode.Walk;

    public DynamicBeaconMode[] BeaconModes { get; } = [DynamicBeaconMode.Walk, DynamicBeaconMode.Drive, DynamicBeaconMode.Custom];

    [ObservableProperty]
    public partial bool IsSaved { get; set; }

    public SettingsViewModel(IConfigurationService configurationService, INavigationService navigationService)
    {
        _configurationService = configurationService;
        _navigationService = navigationService;

        var aprs = _configurationService.Settings.Aprs;
        Callsign = aprs.Callsign;
        DefaultSsid = aprs.DefaultSsid;
        DefaultSymbolTableCharacter = aprs.DefaultSymbolTableCharacter;
        DefaultSymbolCodeCharacter = aprs.DefaultSymbolCodeCharacter;
        DefaultBeaconMode = aprs.DefaultBeaconMode;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        _configurationService.Settings.Aprs.Callsign = Callsign;
        _configurationService.Settings.Aprs.DefaultSsid = DefaultSsid;
        _configurationService.Settings.Aprs.DefaultSymbolTableCharacter = DefaultSymbolTableCharacter;
        _configurationService.Settings.Aprs.DefaultSymbolCodeCharacter = DefaultSymbolCodeCharacter;
        _configurationService.Settings.Aprs.DefaultBeaconMode = DefaultBeaconMode;
        await _configurationService.SaveSettingsAsync();
        IsSaved = true;
    }

    [RelayCommand]
    private void OpenBeaconingSettings()
    {
        _navigationService.NavigateTo<DynamicBeaconingViewModel>();
    }
}
