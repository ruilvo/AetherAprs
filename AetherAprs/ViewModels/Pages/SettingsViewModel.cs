// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Localization;
using AetherAprs.Services.Configuration;
using AetherAprs.Services.UI;
using AetherAprs.ViewModels.Components;
using AetherAprs.ViewModels.Pages;
using AetherAprs.ViewModels.Validation;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AetherAprs.Models.Aprs;
using AetherAprs.Configuration.Settings;

namespace AetherAprs.ViewModels.Pages;

public partial class SettingsViewModel : ViewModelBase, IDisposable
{
    // NOTE: SettingsViewModel is registered as a Singleton in ServiceProviderFactory.
    // IDisposable is implemented to allow proper cleanup during testing or if the registration
    // is changed to Transient in the future. In production, singletons are not disposed by
    // the DI container until app shutdown, so Dispose() is primarily for testing scenarios.
    private readonly IConfigurationService _configurationService;
    private readonly INavigationService _navigationService;
    private readonly MessageTemplatesViewModel _messageTemplatesViewModel;
    private readonly IThemeService? _themeService;
    private readonly ILogger<SettingsViewModel>? _logger;
    private bool _disposed;
    private readonly bool _isInitializing = true;
    private Task _currentSaveTask = Task.CompletedTask;

    [ObservableProperty]
    public partial string Callsign { get; set; } = "N0CALL";

    [ObservableProperty]
    public partial AprsSsid Ssid { get; set; }

    [ObservableProperty]
    public partial string SymbolTableCharacter { get; set; } = "/";

    [ObservableProperty]
    public partial string SymbolCodeCharacter { get; set; } = "[";

    [ObservableProperty]
    public partial string? SymbolOverlayCharacter { get; set; }

    [ObservableProperty]
    public partial AprsSymbolPickerViewModel SymbolPicker { get; set; }

    [ObservableProperty]
    public partial Configuration.PacketDisplayTimeRange DisplayTimeRange { get; set; } = Configuration.PacketDisplayTimeRange.LastDay;

    [ObservableProperty]
    public partial int CustomDisplayTimeRangeHours { get; set; } = 12;

    [ObservableProperty]
    public partial bool LoadHistoryOnStartup { get; set; } = true;

    [ObservableProperty]
    public partial Configuration.ThemeMode Theme { get; set; } = Configuration.ThemeMode.System;

    [ObservableProperty]
    public partial Configuration.TileSourceType TileSource { get; set; } = Configuration.TileSourceType.OsmOnline;

    [ObservableProperty]
    public partial string? MbTilesFilePath { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool EnableDigipeater { get; set; }

    [ObservableProperty]
    public partial bool DigipeaterInsertCallsign { get; set; } = true;

    [ObservableProperty]
    public partial bool EnableAprsIsToRfGate { get; set; }

    [ObservableProperty]
    public partial bool EnableRfToAprsIsGate { get; set; } = true;

    [ObservableProperty]
    public partial int MessageMaxRetries { get; set; } = 5;

    [ObservableProperty]
    public partial int MessageRetryTimeoutSeconds { get; set; } = 30;

    [ObservableProperty]
    public partial int MessageExpiryMinutes { get; set; } = 15;

    [ObservableProperty]
    public partial bool ShowMessageTextInNotifications { get; set; }

    [ObservableProperty]
    public partial bool AutoAcknowledgeMessages { get; set; } = true;

    [ObservableProperty]
    public partial string? BeaconComment { get; set; } = "Using AetherAPRS!";

    [ObservableProperty]
    public partial string? DigipeaterPath { get; set; } = "WIDE1-1,WIDE2-1";

    [ObservableProperty]
    public partial bool DigipeaterRespondToWide1 { get; set; } = false;

    [ObservableProperty]
    public partial bool DigipeaterRespondToWide2 { get; set; } = true;

    [ObservableProperty]
    public partial bool DigipeaterRespondToTrace { get; set; } = false;

    [ObservableProperty]
    public partial PositionFormat PositionFormat { get; set; } = PositionFormat.Compressed;

    public Configuration.PacketDisplayTimeRange[] AvailableTimeRanges { get; } =
        Enum.GetValues<Configuration.PacketDisplayTimeRange>();

    public PositionFormat[] AvailablePositionFormats { get; } =
        Enum.GetValues<PositionFormat>();

    public AprsSsid[] AvailableSsids { get; } =
        Enum.GetValues<AprsSsid>();

    public Configuration.TileSourceType[] AvailableTileSources { get; } =
        Enum.GetValues<Configuration.TileSourceType>();

    public Configuration.ThemeMode[] AvailableThemes { get; } =
        Enum.GetValues<Configuration.ThemeMode>();

    public SettingsViewModel(
        IConfigurationService configurationService,
        INavigationService navigationService,
        AprsSymbolPickerViewModel aprsSymbolPicker,
        MessageTemplatesViewModel messageTemplatesViewModel,
        IThemeService? themeService = null,
        ILogger<SettingsViewModel>? logger = null)
    {
        _configurationService = configurationService;
        _navigationService = navigationService;
        _logger = logger;
        _themeService = themeService;
        _messageTemplatesViewModel = messageTemplatesViewModel;
        SymbolPicker = aprsSymbolPicker;

        AppSettings settings = _configurationService.Current;
        AprsSettings aprs = settings.Aprs;
        Callsign = aprs.Callsign;
        Ssid = aprs.Ssid;
        SymbolTableCharacter = aprs.SymbolTable.ToChar().ToString();
        SymbolCodeCharacter = aprs.SymbolCode.ToChar().ToString();
        SymbolOverlayCharacter = aprs.SymbolOverlay?.ToChar().ToString();
        DisplayTimeRange = aprs.DisplayTimeRange;
        CustomDisplayTimeRangeHours = aprs.CustomDisplayTimeRangeHours;
        LoadHistoryOnStartup = aprs.LoadHistoryOnStartup;
        EnableDigipeater = aprs.EnableDigipeater;
        DigipeaterInsertCallsign = aprs.DigipeaterInsertCallsign;
        EnableAprsIsToRfGate = aprs.EnableAprsIsToRfGate;
        EnableRfToAprsIsGate = aprs.EnableRfToAprsIsGate;
        MessageMaxRetries = aprs.MessageMaxRetries;
        MessageRetryTimeoutSeconds = aprs.MessageRetryTimeoutSeconds;
        MessageExpiryMinutes = aprs.MessageExpiryMinutes;
        ShowMessageTextInNotifications = aprs.ShowMessageTextInNotifications;
        AutoAcknowledgeMessages = aprs.AutoAcknowledgeMessages;
        BeaconComment = aprs.BeaconComment;
        PositionFormat = aprs.PositionFormat;
        DigipeaterPath = aprs.DigipeaterPath;
        DigipeaterRespondToWide1 = aprs.DigipeaterRespondToWide1;
        DigipeaterRespondToWide2 = aprs.DigipeaterRespondToWide2;
        DigipeaterRespondToTrace = aprs.DigipeaterRespondToTrace;

        TileSource = settings.Map.TileSource;
        MbTilesFilePath = settings.Map.MbTilesFilePath;
        Theme = settings.Theme;

        _logger?.LogDebug("Loaded from config: Table={Table}, Code={Code}, Overlay={Overlay}",
            SymbolTableCharacter, SymbolCodeCharacter, SymbolOverlayCharacter);

        // Suppress symbol picker notifications during initialization to prevent async preview updates
        // from triggering property changed events that would overwrite our loaded settings
        SymbolPicker.BeginSuppressNotifications();

        // Initialize symbol picker with settings values
        SymbolPicker.TableCharacter = SymbolTableCharacter;
        SymbolPicker.CodeCharacter = SymbolCodeCharacter;
        SymbolPicker.OverlayCharacter = SymbolOverlayCharacter;

        _logger?.LogDebug("Set in SymbolPicker: Table={Table}, Code={Code}, Overlay={Overlay}",
            SymbolPicker.TableCharacter, SymbolPicker.CodeCharacter, SymbolPicker.OverlayCharacter);

        // Resume notifications and update previews
        SymbolPicker.EndSuppressNotifications();

        // Sync symbol picker changes back to settings (subscribe AFTER initialization)
        SymbolPicker.PropertyChanged += OnSymbolPickerPropertyChanged;

        // Initialization complete - enable auto-save AFTER everything is set up
        _isInitializing = false;
    }

    // Expose the current save task for tests to await completion
    internal Task CurrentSaveTask => _currentSaveTask;

    private void OnSymbolPickerPropertyChanged(object? s, System.ComponentModel.PropertyChangedEventArgs e)
    {
        _logger?.LogDebug("SymbolPicker property changed: {PropertyName}", e.PropertyName);

        if (e.PropertyName == nameof(AprsSymbolPickerViewModel.TableCharacter))
        {
            _logger?.LogDebug("Syncing TableCharacter from SymbolPicker: {Value}", SymbolPicker.TableCharacter);
            SymbolTableCharacter = SymbolPicker.TableCharacter;
        }
        else if (e.PropertyName == nameof(AprsSymbolPickerViewModel.CodeCharacter))
        {
            _logger?.LogDebug("Syncing CodeCharacter from SymbolPicker: {Value}", SymbolPicker.CodeCharacter);
            SymbolCodeCharacter = SymbolPicker.CodeCharacter;
        }
        else if (e.PropertyName == nameof(AprsSymbolPickerViewModel.OverlayCharacter))
        {
            _logger?.LogDebug("Syncing OverlayCharacter from SymbolPicker: {Value}", SymbolPicker.OverlayCharacter);
            SymbolOverlayCharacter = SymbolPicker.OverlayCharacter;
        }
    }

    // Auto-save methods triggered by property changes
    partial void OnCallsignChanged(string value)
    {
        SetErrors(nameof(Callsign), ValidateCallsign(value));
        SaveSettings(settings => settings with { Aprs = settings.Aprs with { Callsign = value } }, nameof(Callsign));
    }

    partial void OnSsidChanged(AprsSsid value)
    {
        SaveSettings(settings => settings with { Aprs = settings.Aprs with { Ssid = value } });
    }

    partial void OnSymbolTableCharacterChanged(string value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            SaveSettings(settings => settings with { Aprs = settings.Aprs with { SymbolTable = value[0].ToSymbolTable() } });
        }
    }

    partial void OnSymbolCodeCharacterChanged(string value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            SaveSettings(settings => settings with { Aprs = settings.Aprs with { SymbolCode = value[0].ToSymbolCode() } });
        }
    }

    partial void OnSymbolOverlayCharacterChanged(string? value)
    {
        SaveSettings(settings => settings with
        {
            Aprs = settings.Aprs with
            {
                SymbolOverlay = string.IsNullOrEmpty(value) ? null : value[0].ToSymbolCode(),
            },
        });
    }

    partial void OnDisplayTimeRangeChanged(Configuration.PacketDisplayTimeRange value)
    {
        SaveSettings(settings => settings with { Aprs = settings.Aprs with { DisplayTimeRange = value } });
    }

    partial void OnCustomDisplayTimeRangeHoursChanged(int value)
    {
        SetErrors(nameof(CustomDisplayTimeRangeHours), ValidateRange(value, 1, 100, Strings.ValidationDisplayHoursRange));
        SaveSettings(settings => settings with { Aprs = settings.Aprs with { CustomDisplayTimeRangeHours = value } }, nameof(CustomDisplayTimeRangeHours));
    }

    partial void OnLoadHistoryOnStartupChanged(bool value)
    {
        SaveSettings(settings => settings with { Aprs = settings.Aprs with { LoadHistoryOnStartup = value } });
    }

    partial void OnPositionFormatChanged(PositionFormat value)
    {
        SaveSettings(settings => settings with { Aprs = settings.Aprs with { PositionFormat = value } });
    }

    partial void OnTileSourceChanged(Configuration.TileSourceType value)
    {
        SaveSettings(settings => settings with { Map = settings.Map with { TileSource = value } });
    }

    partial void OnMbTilesFilePathChanged(string? value)
    {
        SaveSettings(settings => settings with { Map = settings.Map with { MbTilesFilePath = value ?? string.Empty } });
    }

    partial void OnThemeChanged(Configuration.ThemeMode value)
    {
        // Apply immediately so the palette follows the operator's choice while the
        // save is still in flight; the persisted value is the source of truth on restart.
        _themeService?.Apply(value);
        SaveSettings(settings => settings with { Theme = value });
    }

    partial void OnEnableDigipeaterChanged(bool value)
    {
        SaveSettings(settings => settings with { Aprs = settings.Aprs with { EnableDigipeater = value } });
    }

    partial void OnDigipeaterInsertCallsignChanged(bool value)
    {
        SaveSettings(settings => settings with { Aprs = settings.Aprs with { DigipeaterInsertCallsign = value } });
    }

    partial void OnEnableAprsIsToRfGateChanged(bool value)
    {
        SaveSettings(settings => settings with { Aprs = settings.Aprs with { EnableAprsIsToRfGate = value } });
    }

    partial void OnEnableRfToAprsIsGateChanged(bool value)
    {
        SaveSettings(settings => settings with { Aprs = settings.Aprs with { EnableRfToAprsIsGate = value } });
    }

    partial void OnMessageMaxRetriesChanged(int value)
    {
        SetErrors(nameof(MessageMaxRetries), ValidateRange(value, 0, 20, Strings.ValidationMaxRetriesRange));
        SaveSettings(settings => settings with { Aprs = settings.Aprs with { MessageMaxRetries = value } }, nameof(MessageMaxRetries));
    }

    partial void OnMessageRetryTimeoutSecondsChanged(int value)
    {
        SetErrors(nameof(MessageRetryTimeoutSeconds), ValidateRange(value, 5, 300, Strings.ValidationRetryTimeoutRange));
        SaveSettings(settings => settings with { Aprs = settings.Aprs with { MessageRetryTimeoutSeconds = value } }, nameof(MessageRetryTimeoutSeconds));
    }

    partial void OnMessageExpiryMinutesChanged(int value)
    {
        SetErrors(nameof(MessageExpiryMinutes), ValidateRange(value, 1, 1440, Strings.ValidationExpiryMinutesRange));
        SaveSettings(settings => settings with { Aprs = settings.Aprs with { MessageExpiryMinutes = value } }, nameof(MessageExpiryMinutes));
    }

    partial void OnShowMessageTextInNotificationsChanged(bool value)
    {
        SaveSettings(settings => settings with { Aprs = settings.Aprs with { ShowMessageTextInNotifications = value } });
    }

    partial void OnAutoAcknowledgeMessagesChanged(bool value)
    {
        SaveSettings(settings => settings with { Aprs = settings.Aprs with { AutoAcknowledgeMessages = value } });
    }

    partial void OnBeaconCommentChanged(string? value)
    {
        SetErrors(nameof(BeaconComment), ValidateBeaconComment(value));
        SaveSettings(settings => settings with { Aprs = settings.Aprs with { BeaconComment = value } }, nameof(BeaconComment));
    }

    partial void OnDigipeaterPathChanged(string? value)
    {
        SetErrors(nameof(DigipeaterPath), ValidateDigipeaterPath(value));
        SaveSettings(settings => settings with { Aprs = settings.Aprs with { DigipeaterPath = value } }, nameof(DigipeaterPath));
    }

    /// <summary>Longest beacon comment the APRS status/position encoders accept.</summary>
    private const int BeaconCommentMaxLength = 43;

    private static readonly AprsPathAttribute DigipeaterPathRule = new();

    [GeneratedRegex("^[A-Z0-9]{1,6}$")]
    private static partial Regex CallsignFormatPattern();

    private static IReadOnlyList<string> ValidateCallsign(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [Strings.ValidationCallsignRequired];
        }

        return CallsignFormatPattern().IsMatch(value)
            ? []
            : [Strings.ValidationCallsignFormat];
    }

    private static IReadOnlyList<string> ValidateRange(
        int value,
        int minimum,
        int maximum,
        string message)
    {
        return value >= minimum && value <= maximum ? [] : [message];
    }

    private static IReadOnlyList<string> ValidateBeaconComment(string? value)
    {
        return value is null || value.Length <= BeaconCommentMaxLength
            ? []
            : [Strings.ValidationCommentMaxLength];
    }

    private static IReadOnlyList<string> ValidateDigipeaterPath(string? value)
    {
        return DigipeaterPathRule.IsValid(value)
            ? []
            : [Strings.ValidationDigipeaterPathFormat];
    }

    partial void OnDigipeaterRespondToWide1Changed(bool value)
    {
        SaveSettings(settings => settings with { Aprs = settings.Aprs with { DigipeaterRespondToWide1 = value } });
    }

    partial void OnDigipeaterRespondToWide2Changed(bool value)
    {
        SaveSettings(settings => settings with { Aprs = settings.Aprs with { DigipeaterRespondToWide2 = value } });
    }

    partial void OnDigipeaterRespondToTraceChanged(bool value)
    {
        SaveSettings(settings => settings with { Aprs = settings.Aprs with { DigipeaterRespondToTrace = value } });
    }

    private void SaveSettings(
        Func<Configuration.Settings.AppSettings, Configuration.Settings.AppSettings> update,
        string? validatedProperty = null)
    {
        if (_isInitializing || _disposed ||
            (validatedProperty is not null && GetErrors(validatedProperty).Cast<string>().Any()))
        {
            return;
        }

        _currentSaveTask = SaveAfterAsync(_currentSaveTask, update);
    }

    private async Task SaveAfterAsync(
        Task previousSave,
        Func<Configuration.Settings.AppSettings, Configuration.Settings.AppSettings> update)
    {
        try
        {
            await previousSave.ConfigureAwait(false);
            await _configurationService.UpdateAsync(update).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error saving settings");
        }
    }

    [RelayCommand]
    private void OpenBeaconingSettings() => _navigationService.NavigateTo<DynamicBeaconingViewModel>();

    [RelayCommand]
    private void OpenDownloadOfflineMap() => _navigationService.NavigateTo<DownloadOfflineMapViewModel>();

    [RelayCommand]
    private void OpenSupport() => _navigationService.NavigateTo<SupportViewModel>();

    [RelayCommand]
    private void OpenMessageTemplates() => _navigationService.NavigateTo(_messageTemplatesViewModel);

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        SymbolPicker?.PropertyChanged -= OnSymbolPickerPropertyChanged;
    }
}
