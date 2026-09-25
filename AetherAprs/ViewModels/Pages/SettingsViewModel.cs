// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Localization;
using AetherAprs.Services;
using AetherAprs.ViewModels.Components;
using AetherAprs.ViewModels.Pages;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System;
using System.ComponentModel.DataAnnotations;

namespace AetherAprs.ViewModels;

public partial class SettingsViewModel : ViewModelBase, IDisposable
{
    private readonly IConfigurationService _configurationService;
    private readonly INavigationService _navigationService;
    private readonly ILogger<SettingsViewModel>? _logger;
    private bool _disposed;
    private bool _isInitializing = true;

    [ObservableProperty]
    [Required(ErrorMessageResourceName = nameof(Strings.ValidationCallsignRequired), ErrorMessageResourceType = typeof(Strings))]
    [RegularExpression(@"^[A-Z0-9]{1,6}$", ErrorMessageResourceName = nameof(Strings.ValidationCallsignFormat), ErrorMessageResourceType = typeof(Strings))]
    [NotifyDataErrorInfo]
    public partial string Callsign { get; set; } = "N0CALL";

    [ObservableProperty]
    [Range(0, 15, ErrorMessageResourceName = nameof(Strings.ValidationSsidRange), ErrorMessageResourceType = typeof(Strings))]
    [NotifyDataErrorInfo]
    public partial int Ssid { get; set; }

    [ObservableProperty]
    public partial string DefaultSymbolTableCharacter { get; set; } = "/";

    [ObservableProperty]
    public partial string DefaultSymbolCodeCharacter { get; set; } = "[";

    [ObservableProperty]
    public partial string? DefaultSymbolOverlayCharacter { get; set; }

    [ObservableProperty]
    public partial AprsSymbolPickerViewModel SymbolPicker { get; set; }

    [ObservableProperty]
    public partial Configuration.PacketDisplayTimeRange DisplayTimeRange { get; set; } = Configuration.PacketDisplayTimeRange.LastDay;

    [ObservableProperty]
    [Range(1, 100, ErrorMessageResourceName = nameof(Strings.ValidationDisplayHoursRange), ErrorMessageResourceType = typeof(Strings))]
    [NotifyDataErrorInfo]
    public partial int CustomDisplayTimeRangeHours { get; set; } = 12;

    [ObservableProperty]
    public partial bool EnableDigipeater { get; set; }

    [ObservableProperty]
    public partial bool DigipeaterInsertCallsign { get; set; } = true;

    [ObservableProperty]
    public partial bool EnableAprsIsToRfGate { get; set; }

    [ObservableProperty]
    public partial bool EnableRfToAprsIsGate { get; set; } = true;

    [ObservableProperty]
    [Range(0, 20, ErrorMessageResourceName = nameof(Strings.ValidationMaxRetriesRange), ErrorMessageResourceType = typeof(Strings))]
    [NotifyDataErrorInfo]
    public partial int MessageMaxRetries { get; set; } = 5;

    [ObservableProperty]
    [Range(5, 300, ErrorMessageResourceName = nameof(Strings.ValidationRetryTimeoutRange), ErrorMessageResourceType = typeof(Strings))]
    [NotifyDataErrorInfo]
    public partial int MessageRetryTimeoutSeconds { get; set; } = 30;

    [ObservableProperty]
    public partial bool AutoAcknowledgeMessages { get; set; } = true;

    [ObservableProperty]
    [MaxLength(43, ErrorMessageResourceName = nameof(Strings.ValidationCommentMaxLength), ErrorMessageResourceType = typeof(Strings))]
    [NotifyDataErrorInfo]
    public partial string? DefaultBeaconComment { get; set; } = "Using AetherAPRS!";

    [ObservableProperty]
    public partial string? DigipeaterPath { get; set; } = "WIDE1-1,WIDE2-1";

    [ObservableProperty]
    public partial bool DigipeaterRespondToWide1 { get; set; } = false;

    [ObservableProperty]
    public partial bool DigipeaterRespondToWide2 { get; set; } = true;

    public Configuration.PacketDisplayTimeRange[] AvailableTimeRanges { get; } = 
        (Configuration.PacketDisplayTimeRange[])Enum.GetValues(typeof(Configuration.PacketDisplayTimeRange));

    public SettingsViewModel(
        IConfigurationService configurationService,
        INavigationService navigationService,
        AprsSymbolPickerViewModel aprsSymbolPicker,
        ILogger<SettingsViewModel>? logger = null)
    {
        _configurationService = configurationService;
        _navigationService = navigationService;
        _logger = logger;
        SymbolPicker = aprsSymbolPicker;

        var aprs = _configurationService.Settings.Aprs;
        Callsign = aprs.Callsign;
        Ssid = aprs.DefaultSsid;
        DefaultSymbolTableCharacter = aprs.DefaultSymbolTableCharacter;
        DefaultSymbolCodeCharacter = aprs.DefaultSymbolCodeCharacter;
        DefaultSymbolOverlayCharacter = aprs.DefaultSymbolOverlayCharacter;
        DisplayTimeRange = aprs.DisplayTimeRange;
        CustomDisplayTimeRangeHours = aprs.CustomDisplayTimeRangeHours;
        EnableDigipeater = aprs.EnableDigipeater;
        DigipeaterInsertCallsign = aprs.DigipeaterInsertCallsign;
        EnableAprsIsToRfGate = aprs.EnableAprsIsToRfGate;
        EnableRfToAprsIsGate = aprs.EnableRfToAprsIsGate;
        MessageMaxRetries = aprs.MessageMaxRetries;
        MessageRetryTimeoutSeconds = aprs.MessageRetryTimeoutSeconds;
        AutoAcknowledgeMessages = aprs.AutoAcknowledgeMessages;
        DefaultBeaconComment = aprs.DefaultBeaconComment;
        DigipeaterPath = aprs.DigipeaterPath;
        DigipeaterRespondToWide1 = aprs.DigipeaterRespondToWide1;
        DigipeaterRespondToWide2 = aprs.DigipeaterRespondToWide2;

        _logger?.LogDebug("Loaded from config: Table={Table}, Code={Code}, Overlay={Overlay}", 
            DefaultSymbolTableCharacter, DefaultSymbolCodeCharacter, DefaultSymbolOverlayCharacter);

        // Suppress symbol picker notifications during initialization to prevent async preview updates
        // from triggering property changed events that would overwrite our loaded settings
        SymbolPicker.BeginSuppressNotifications();
        
        // Initialize symbol picker with settings values
        SymbolPicker.TableCharacter = DefaultSymbolTableCharacter;
        SymbolPicker.CodeCharacter = DefaultSymbolCodeCharacter;
        SymbolPicker.OverlayCharacter = DefaultSymbolOverlayCharacter;

        _logger?.LogDebug("Set in SymbolPicker: Table={Table}, Code={Code}, Overlay={Overlay}", 
            SymbolPicker.TableCharacter, SymbolPicker.CodeCharacter, SymbolPicker.OverlayCharacter);

        // Resume notifications and update previews
        SymbolPicker.EndSuppressNotifications();

        // Sync symbol picker changes back to settings (subscribe AFTER initialization)
        SymbolPicker.PropertyChanged += OnSymbolPickerPropertyChanged;

        // Initialization complete - enable auto-save AFTER everything is set up
        _isInitializing = false;
    }

    private void OnSymbolPickerPropertyChanged(object? s, System.ComponentModel.PropertyChangedEventArgs e)
    {
        _logger?.LogDebug("SymbolPicker property changed: {PropertyName}", e.PropertyName);
        
        if (e.PropertyName == nameof(AprsSymbolPickerViewModel.TableCharacter))
        {
            _logger?.LogDebug("Syncing TableCharacter from SymbolPicker: {Value}", SymbolPicker.TableCharacter);
            DefaultSymbolTableCharacter = SymbolPicker.TableCharacter;
        }
        else if (e.PropertyName == nameof(AprsSymbolPickerViewModel.CodeCharacter))
        {
            _logger?.LogDebug("Syncing CodeCharacter from SymbolPicker: {Value}", SymbolPicker.CodeCharacter);
            DefaultSymbolCodeCharacter = SymbolPicker.CodeCharacter;
        }
        else if (e.PropertyName == nameof(AprsSymbolPickerViewModel.OverlayCharacter))
        {
            _logger?.LogDebug("Syncing OverlayCharacter from SymbolPicker: {Value}", SymbolPicker.OverlayCharacter);
            DefaultSymbolOverlayCharacter = SymbolPicker.OverlayCharacter;
        }
    }

    // Auto-save methods triggered by property changes
    partial void OnCallsignChanged(string value)
    {
        SaveSettings();
    }

    partial void OnSsidChanged(int value)
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

    partial void OnDisplayTimeRangeChanged(Configuration.PacketDisplayTimeRange value)
    {
        SaveSettings();
    }

    partial void OnCustomDisplayTimeRangeHoursChanged(int value)
    {
        SaveSettings();
    }

    partial void OnEnableDigipeaterChanged(bool value)
    {
        SaveSettings();
    }

    partial void OnDigipeaterInsertCallsignChanged(bool value)
    {
        SaveSettings();
    }

    partial void OnEnableAprsIsToRfGateChanged(bool value)
    {
        SaveSettings();
    }

    partial void OnEnableRfToAprsIsGateChanged(bool value)
    {
        SaveSettings();
    }

    partial void OnMessageMaxRetriesChanged(int value)
    {
        SaveSettings();
    }

    partial void OnMessageRetryTimeoutSecondsChanged(int value)
    {
        SaveSettings();
    }

    partial void OnAutoAcknowledgeMessagesChanged(bool value)
    {
        SaveSettings();
    }

    partial void OnDefaultBeaconCommentChanged(string? value)
    {
        SaveSettings();
    }

    partial void OnDigipeaterPathChanged(string? value)
    {
        SaveSettings();
    }

    partial void OnDigipeaterRespondToWide1Changed(bool value)
    {
        SaveSettings();
    }

    partial void OnDigipeaterRespondToWide2Changed(bool value)
    {
        SaveSettings();
    }

    private void SaveSettings()
    {
        // Don't auto-save during initialization
        if (_isInitializing)
        {
            return;
        }

        // Don't save if there are validation errors
        if (HasErrors)
        {
            _logger?.LogDebug("Skipping save due to validation errors");
            return;
        }

        try
        {
            _configurationService.Settings.Aprs.Callsign = Callsign;
            _configurationService.Settings.Aprs.DefaultSsid = Ssid;
            _configurationService.Settings.Aprs.DefaultSymbolTableCharacter = DefaultSymbolTableCharacter;
            _configurationService.Settings.Aprs.DefaultSymbolCodeCharacter = DefaultSymbolCodeCharacter;
            _configurationService.Settings.Aprs.DefaultSymbolOverlayCharacter = DefaultSymbolOverlayCharacter;
            _configurationService.Settings.Aprs.DisplayTimeRange = DisplayTimeRange;
            _configurationService.Settings.Aprs.CustomDisplayTimeRangeHours = CustomDisplayTimeRangeHours;
            _configurationService.Settings.Aprs.EnableDigipeater = EnableDigipeater;
            _configurationService.Settings.Aprs.DigipeaterInsertCallsign = DigipeaterInsertCallsign;
            _configurationService.Settings.Aprs.EnableAprsIsToRfGate = EnableAprsIsToRfGate;
            _configurationService.Settings.Aprs.EnableRfToAprsIsGate = EnableRfToAprsIsGate;
            _configurationService.Settings.Aprs.MessageMaxRetries = MessageMaxRetries;
            _configurationService.Settings.Aprs.MessageRetryTimeoutSeconds = MessageRetryTimeoutSeconds;
            _configurationService.Settings.Aprs.AutoAcknowledgeMessages = AutoAcknowledgeMessages;
            _configurationService.Settings.Aprs.DefaultBeaconComment = DefaultBeaconComment;
            _configurationService.Settings.Aprs.DigipeaterPath = DigipeaterPath;
            _configurationService.Settings.Aprs.DigipeaterRespondToWide1 = DigipeaterRespondToWide1;
            _configurationService.Settings.Aprs.DigipeaterRespondToWide2 = DigipeaterRespondToWide2;

            // Fire-and-forget is acceptable here as we don't need to wait for save completion
            _ = _configurationService.SaveSettingsAsync();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error saving settings");
        }
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
