// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Configuration;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

namespace AetherAprs.ViewModels;

public partial class PortItemViewModel : ViewModelBase
{
    private readonly PortConfig _config;
    private readonly Func<PortItemViewModel, Task> _onToggle;
    private readonly Func<PortItemViewModel, Task> _onShowOnMapToggle;
    private readonly Func<PortItemViewModel, Task> _onDelete;
    private readonly Action<PortItemViewModel> _onEdit;
    private readonly ILogger<PortItemViewModel>? _logger;
    private bool _isInitializing;

    public Guid Id => _config.Id;

    public string Name => _config.Name;

    public string TypeName => _config.TypeSettings switch
    {
        AprsIsSettings => Localization.Strings.Get("PortTypeAprsIs"),
        KissSettings => Localization.Strings.Get("PortTypeKiss"),
        _ => Localization.Strings.Get("PortTypeUnknown")
    };

    public string StatusText => IsEnabled
        ? Localization.Strings.Get("Running")
        : Localization.Strings.Get("Stopped");

    [ObservableProperty]
    public partial bool IsEnabled { get; set; }

    [ObservableProperty]
    public partial bool ShowOnMap { get; set; }

    public PortItemViewModel(
        PortConfig config,
        Func<PortItemViewModel, Task> onToggle,
        Func<PortItemViewModel, Task> onShowOnMapToggle,
        Func<PortItemViewModel, Task> onDelete,
        Action<PortItemViewModel> onEdit,
        ILogger<PortItemViewModel>? logger = null)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _onToggle = onToggle;
        _onShowOnMapToggle = onShowOnMapToggle;
        _onDelete = onDelete;
        _onEdit = onEdit;
        _logger = logger;

        _isInitializing = true;
        IsEnabled = config.IsEnabled;
        ShowOnMap = config.ShowOnMap;
        _isInitializing = false;
    }

    partial void OnIsEnabledChanged(bool value)
    {
        OnPropertyChanged(nameof(StatusText));
        if (!_isInitializing)
        {
            _ = HandleToggleAsync();
        }
    }

    partial void OnShowOnMapChanged(bool value)
    {
        if (!_isInitializing)
        {
            _ = HandleShowOnMapToggleAsync();
        }
    }

    private async Task HandleToggleAsync()
    {
        try
        {
            await _onToggle(this);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error toggling port {PortName} (ID: {PortId})", Name, Id);
            // Revert the toggle on failure
            _isInitializing = true;
            IsEnabled = !IsEnabled;
            _isInitializing = false;
        }
    }

    private async Task HandleShowOnMapToggleAsync()
    {
        try
        {
            await _onShowOnMapToggle(this);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error toggling ShowOnMap for port {PortName} (ID: {PortId})", Name, Id);
            // Revert on failure
            _isInitializing = true;
            ShowOnMap = !ShowOnMap;
            _isInitializing = false;
        }
    }

    [RelayCommand]
    private void Edit()
    {
        _onEdit(this);
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        await _onDelete(this);
    }

    [RelayCommand]
    private void ToggleShowOnMap()
    {
        ShowOnMap = !ShowOnMap;
    }

    public PortConfig BuildConfig()
    {
        _config.IsEnabled = IsEnabled;
        _config.ShowOnMap = ShowOnMap;
        return _config;
    }
}
