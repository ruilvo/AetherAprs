// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Imaging;
using AetherAprs.Services;
using AetherAprs.ViewModels.Components;
using AetherAprs.ViewModels.Pages;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;

namespace AetherAprs.ViewModels;

public partial class SettingsViewModel : ViewModelBase, IDisposable
{
    private readonly IConfigurationService _configurationService;
    private readonly INavigationService _navigationService;
    private bool _disposed;

    [ObservableProperty]
    public partial string Callsign { get; set; } = "N0CALL";

    [ObservableProperty]
    public partial int? DefaultSsid { get; set; }

    [ObservableProperty]
    public partial string DefaultSymbolTableCharacter { get; set; } = "/";

    [ObservableProperty]
    public partial string DefaultSymbolCodeCharacter { get; set; } = "[";

    [ObservableProperty]
    public partial string? DefaultSymbolOverlayCharacter { get; set; }

    [ObservableProperty]
    public partial AprsSymbolPickerViewModel SymbolPicker { get; set; }

    public SettingsViewModel(
        IConfigurationService configurationService,
        INavigationService navigationService,
        IAprsSymbolBitmapProvider symbolBitmapProvider)
    {
        _configurationService = configurationService;
        _navigationService = navigationService;

        var aprs = _configurationService.Settings.Aprs;
        Callsign = aprs.Callsign;
        DefaultSsid = aprs.DefaultSsid;
        DefaultSymbolTableCharacter = aprs.DefaultSymbolTableCharacter;
        DefaultSymbolCodeCharacter = aprs.DefaultSymbolCodeCharacter;
        DefaultSymbolOverlayCharacter = aprs.DefaultSymbolOverlayCharacter;

        // Initialize symbol picker ViewModel
        SymbolPicker = new AprsSymbolPickerViewModel(symbolBitmapProvider)
        {
            TableCharacter = DefaultSymbolTableCharacter,
            CodeCharacter = DefaultSymbolCodeCharacter,
            OverlayCharacter = DefaultSymbolOverlayCharacter
        };

        // Sync symbol picker changes back to settings
        SymbolPicker.PropertyChanged += OnSymbolPickerPropertyChanged;
    }

    private void OnSymbolPickerPropertyChanged(object? s, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AprsSymbolPickerViewModel.TableCharacter))
        {
            DefaultSymbolTableCharacter = SymbolPicker.TableCharacter;
        }
        else if (e.PropertyName == nameof(AprsSymbolPickerViewModel.CodeCharacter))
        {
            DefaultSymbolCodeCharacter = SymbolPicker.CodeCharacter;
        }
        else if (e.PropertyName == nameof(AprsSymbolPickerViewModel.OverlayCharacter))
        {
            DefaultSymbolOverlayCharacter = SymbolPicker.OverlayCharacter;
        }
    }

    // Auto-save methods triggered by property changes
    partial void OnCallsignChanged(string value)
    {
        SaveSettings();
    }

    partial void OnDefaultSsidChanged(int? value)
    {
        SaveSettings();
    }

    partial void OnDefaultSymbolTableCharacterChanged(string value)
    {
        SaveSettings();
    }

    partial void OnDefaultSymbolCodeCharacterChanged(string value)
    {
        SaveSettings();
    }

    partial void OnDefaultSymbolOverlayCharacterChanged(string? value)
    {
        SaveSettings();
    }

    private void SaveSettings()
    {
        _configurationService.Settings.Aprs.Callsign = Callsign;
        _configurationService.Settings.Aprs.DefaultSsid = DefaultSsid ?? 0;
        _configurationService.Settings.Aprs.DefaultSymbolTableCharacter = DefaultSymbolTableCharacter;
        _configurationService.Settings.Aprs.DefaultSymbolCodeCharacter = DefaultSymbolCodeCharacter;
        _configurationService.Settings.Aprs.DefaultSymbolOverlayCharacter = DefaultSymbolOverlayCharacter;

        // Fire-and-forget is acceptable here as we don't need to wait for save completion
        _ = _configurationService.SaveSettingsAsync();
    }

    [RelayCommand]
    private void OpenBeaconingSettings()
    {
        _navigationService.NavigateTo<DynamicBeaconingViewModel>();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (SymbolPicker != null)
        {
            SymbolPicker.PropertyChanged -= OnSymbolPickerPropertyChanged;
        }
    }
}
