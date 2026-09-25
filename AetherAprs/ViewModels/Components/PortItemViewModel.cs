// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Configuration;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;

namespace AetherAprs.ViewModels;

public partial class PortItemViewModel : ViewModelBase
{
    private readonly PortConfig _config;
    private readonly Action<PortItemViewModel> _onToggle;
    private readonly Action<PortItemViewModel> _onShowOnMapToggle;
    private readonly Action<PortItemViewModel> _onDelete;
    private readonly Action<PortItemViewModel> _onEdit;
    private readonly bool _isInitializing;

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
        Action<PortItemViewModel> onToggle,
        Action<PortItemViewModel> onShowOnMapToggle,
        Action<PortItemViewModel> onDelete,
        Action<PortItemViewModel> onEdit)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _onToggle = onToggle;
        _onShowOnMapToggle = onShowOnMapToggle;
        _onDelete = onDelete;
        _onEdit = onEdit;

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
            _onToggle(this);
        }
    }

    partial void OnShowOnMapChanged(bool value)
    {
        if (!_isInitializing)
        {
            _onShowOnMapToggle(this);
        }
    }

    [RelayCommand]
    private void Edit()
    {
        _onEdit(this);
    }

    [RelayCommand]
    private void Delete()
    {
        _onDelete(this);
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
