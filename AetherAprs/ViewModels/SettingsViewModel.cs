// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Services.Configuration;
using AetherAprs.Services.Logging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace AetherAprs.ViewModels;

/// <summary>
/// View model for the settings page, allowing users to configure application preferences.
/// </summary>
public partial class SettingsViewModel : ViewModelBase
{
    private readonly IConfigurationService _configurationService;
    private readonly ILoggingService _log;

    [ObservableProperty]
    public required partial LogLevel SelectedLogLevel { get; set; }

    [ObservableProperty]
    public required partial bool WriteToFile { get; set; }

    [ObservableProperty]
    public required partial string Callsign { get; set; }

    [ObservableProperty]
    public required partial string Passcode { get; set; }

    [ObservableProperty]
    public required partial string Filter { get; set; }

    [ObservableProperty]
    public required partial string? StatusMessage { get; set; }

    /// <summary>
    /// Gets the available log levels for display in the UI.
    /// </summary>
    public IReadOnlyList<LogLevel> LogLevels { get; } =
        Enum.GetValues<LogLevel>().ToArray();

    [SetsRequiredMembers]
    public SettingsViewModel(IConfigurationService configurationService, ILoggingService loggingService)
    {
        _configurationService = configurationService;
        _log = loggingService.ForContext(nameof(SettingsViewModel));
        _log.Debug("Constructed.");

        var settings = _configurationService.Settings;
        SelectedLogLevel = settings.Logging.LogLevel;
        WriteToFile = settings.Logging.WriteToFile;
        Callsign = settings.AprsIs.Callsign;
        Passcode = settings.AprsIs.Passcode;
        Filter = settings.AprsIs.Filter;
        StatusMessage = null;
    }

    [RelayCommand]
    private void Save()
    {
        _log.Info("User requested settings save.");
        var newSettings = _configurationService.Settings.Clone();
        newSettings.Logging.LogLevel = SelectedLogLevel;
        newSettings.Logging.WriteToFile = WriteToFile;
        newSettings.AprsIs.Callsign = Callsign;
        newSettings.AprsIs.Passcode = Passcode;
        newSettings.AprsIs.Filter = Filter;

        if (_configurationService.SaveSettings(newSettings))
        {
            _log.Info($"Settings saved (LogLevel={SelectedLogLevel}, WriteToFile={WriteToFile}, Callsign={Callsign}).");
            StatusMessage = "Settings saved.";
        }
        else
        {
            _log.Error("Settings save failed.");
            StatusMessage = "Failed to save settings.";
        }
    }

    [RelayCommand]
    private void Reset()
    {
        _log.Info("User requested settings reset to defaults.");
        var defaults = _configurationService.GetDefaults();
        SelectedLogLevel = defaults.Logging.LogLevel;
        WriteToFile = defaults.Logging.WriteToFile;
        Callsign = defaults.AprsIs.Callsign;
        Passcode = defaults.AprsIs.Passcode;
        Filter = defaults.AprsIs.Filter;
        StatusMessage = null;
    }
}
