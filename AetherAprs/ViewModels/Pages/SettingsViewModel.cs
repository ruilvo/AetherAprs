// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Localization;
using AetherAprs.Services.Configuration;
using AetherAprs.Services.UI;
using AetherAprs.ViewModels.Components;
using AetherAprs.ViewModels.Pages;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using AetherAprs.Models.Aprs;

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
    private readonly ILogger<SettingsViewModel>? _logger;
    private bool _disposed;
    private bool _isInitializing = true;
    private Task _currentSaveTask = Task.CompletedTask;

    [ObservableProperty]
    [Required(ErrorMessageResourceName = nameof(Strings.ValidationCallsignRequired), ErrorMessageResourceType = typeof(Strings))]
    [RegularExpression(@"^[A-Z0-9]{1,6}$", ErrorMessageResourceName = nameof(Strings.ValidationCallsignFormat), ErrorMessageResourceType = typeof(Strings))]
    [NotifyDataErrorInfo]
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
    [Range(1, 100, ErrorMessageResourceName = nameof(Strings.ValidationDisplayHoursRange), ErrorMessageResourceType = typeof(Strings))]
    [NotifyDataErrorInfo]
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
        ILogger<SettingsViewModel>? logger = null)
    {
        _configurationService = configurationService;
        _navigationService = navigationService;
        _logger = logger;
        _messageTemplatesViewModel = messageTemplatesViewModel;
        SymbolPicker = aprsSymbolPicker;

        var settings = _configurationService.GetSettingsSnapshot();
        var aprs = settings.Aprs;
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
        SaveSettings(settings => settings.Aprs.Callsign = value, nameof(Callsign));
    }

    partial void OnSsidChanged(AprsSsid value)
    {
        SaveSettings(settings => settings.Aprs.Ssid = value);
    }

    partial void OnSymbolTableCharacterChanged(string value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            SaveSettings(settings => settings.Aprs.SymbolTable = value[0].ToSymbolTable());
        }
    }

    partial void OnSymbolCodeCharacterChanged(string value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            SaveSettings(settings => settings.Aprs.SymbolCode = value[0].ToSymbolCode());
        }
    }

    partial void OnSymbolOverlayCharacterChanged(string? value)
    {
        SaveSettings(settings => settings.Aprs.SymbolOverlay =
            string.IsNullOrEmpty(value) ? null : value[0].ToSymbolCode());
    }

    partial void OnDisplayTimeRangeChanged(Configuration.PacketDisplayTimeRange value)
    {
        SaveSettings(settings => settings.Aprs.DisplayTimeRange = value);
    }

    partial void OnCustomDisplayTimeRangeHoursChanged(int value)
    {
        SaveSettings(settings => settings.Aprs.CustomDisplayTimeRangeHours = value, nameof(CustomDisplayTimeRangeHours));
    }

    partial void OnLoadHistoryOnStartupChanged(bool value)
    {
        SaveSettings(settings => settings.Aprs.LoadHistoryOnStartup = value);
    }

    partial void OnPositionFormatChanged(PositionFormat value)
    {
        SaveSettings(settings => settings.Aprs.PositionFormat = value);
    }

    partial void OnTileSourceChanged(Configuration.TileSourceType value)
    {
        SaveSettings(settings => settings.Map.TileSource = value);
    }

    partial void OnMbTilesFilePathChanged(string? value)
    {
        SaveSettings(settings => settings.Map.MbTilesFilePath = value ?? string.Empty);
    }

    partial void OnThemeChanged(Configuration.ThemeMode value)
    {
        App.ApplyThemeMode(value);
        SaveSettings(settings => settings.Theme = value);
    }

    partial void OnEnableDigipeaterChanged(bool value)
    {
        SaveSettings(settings => settings.Aprs.EnableDigipeater = value);
    }

    partial void OnDigipeaterInsertCallsignChanged(bool value)
    {
        SaveSettings(settings => settings.Aprs.DigipeaterInsertCallsign = value);
    }

    partial void OnEnableAprsIsToRfGateChanged(bool value)
    {
        SaveSettings(settings => settings.Aprs.EnableAprsIsToRfGate = value);
    }

    partial void OnEnableRfToAprsIsGateChanged(bool value)
    {
        SaveSettings(settings => settings.Aprs.EnableRfToAprsIsGate = value);
    }

    partial void OnMessageMaxRetriesChanged(int value)
    {
        SaveSettings(settings => settings.Aprs.MessageMaxRetries = value, nameof(MessageMaxRetries));
    }

    partial void OnMessageRetryTimeoutSecondsChanged(int value)
    {
        SaveSettings(settings => settings.Aprs.MessageRetryTimeoutSeconds = value, nameof(MessageRetryTimeoutSeconds));
    }

    partial void OnAutoAcknowledgeMessagesChanged(bool value)
    {
        SaveSettings(settings => settings.Aprs.AutoAcknowledgeMessages = value);
    }

    partial void OnBeaconCommentChanged(string? value)
    {
        SaveSettings(settings => settings.Aprs.BeaconComment = value, nameof(BeaconComment));
    }

    partial void OnDigipeaterPathChanged(string? value)
    {
        SaveSettings(settings => settings.Aprs.DigipeaterPath = value);
    }

    partial void OnDigipeaterRespondToWide1Changed(bool value)
    {
        SaveSettings(settings => settings.Aprs.DigipeaterRespondToWide1 = value);
    }

    partial void OnDigipeaterRespondToWide2Changed(bool value)
    {
        SaveSettings(settings => settings.Aprs.DigipeaterRespondToWide2 = value);
    }

    partial void OnDigipeaterRespondToTraceChanged(bool value)
    {
        SaveSettings(settings => settings.Aprs.DigipeaterRespondToTrace = value);
    }

    private void SaveSettings(Action<Configuration.Settings.AppSettings> update, string? validatedProperty = null)
    {
        if (_isInitializing || _disposed ||
            (validatedProperty is not null && GetErrors(validatedProperty).Any()))
        {
            return;
        }

        _currentSaveTask = SaveAfterAsync(_currentSaveTask, update);
    }

    private async Task SaveAfterAsync(Task previousSave, Action<Configuration.Settings.AppSettings> update)
    {
        try
        {
            await previousSave.ConfigureAwait(false);
            await _configurationService.UpdateSettingsAsync(update).ConfigureAwait(false);
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

    [RelayCommand]
    private void OpenDownloadOfflineMap()
    {
        _navigationService.NavigateTo<DownloadOfflineMapViewModel>();
    }

    [RelayCommand]
    private void OpenMessageTemplates()
    {
        _navigationService.NavigateTo(_messageTemplatesViewModel);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
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
