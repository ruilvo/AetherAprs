// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Services.Configuration;
using AetherAprs.Services.Logging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AetherAprs.ViewModels;

/// <summary>
/// View model for the settings page, allowing users to configure application preferences.
/// </summary>
public partial class SettingsViewModel : ViewModelBase
{
    private readonly IConfigurationService _configurationService;

    [ObservableProperty]
    private LogLevel _selectedLogLevel;

    [ObservableProperty]
    private bool _writeToFile;

    [ObservableProperty]
    private string _callsign = string.Empty;

    [ObservableProperty]
    private string _passcode = string.Empty;

    [ObservableProperty]
    private string _filter = string.Empty;

    [ObservableProperty]
    private string? _statusMessage;

    /// <summary>
    /// Gets the available log levels for display in the UI.
    /// </summary>
    public IReadOnlyList<LogLevel> LogLevels { get; } =
        Enum.GetValues<LogLevel>().ToArray();

    public SettingsViewModel(IConfigurationService configurationService)
    {
        _configurationService = configurationService;

        // Load current settings
        var settings = _configurationService.Settings;
        _selectedLogLevel = settings.Logging.LogLevel;
        _writeToFile = settings.Logging.WriteToFile;
        _callsign = settings.AprsIs.Callsign;
        _passcode = settings.AprsIs.Passcode;
        _filter = settings.AprsIs.Filter;
    }

    [RelayCommand]
    private void Save()
    {
        var newSettings = new AppSettings
        {
            Logging = new LoggingSettings
            {
                LogLevel = SelectedLogLevel,
                WriteToFile = WriteToFile,
            },
            AprsIs = new AprsSettings
            {
                Callsign = Callsign,
                Passcode = Passcode,
                Filter = Filter,
            },
        };

        if (_configurationService.SaveSettings(newSettings))
        {
            StatusMessage = "Settings saved.";
        }
        else
        {
            StatusMessage = "Failed to save settings.";
        }
    }

    [RelayCommand]
    private void Reset()
    {
        var defaults = new AppSettings();
        SelectedLogLevel = defaults.Logging.LogLevel;
        WriteToFile = defaults.Logging.WriteToFile;
        Callsign = defaults.AprsIs.Callsign;
        Passcode = defaults.AprsIs.Passcode;
        Filter = defaults.AprsIs.Filter;
        StatusMessage = null;
    }
}
