// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using CommunityToolkit.Mvvm.ComponentModel;
using AetherAprs.Configuration;

namespace AetherAprs.ViewModels;

public partial class PortItemViewModel(PortConfig config, Action<PortItemViewModel> onToggle, Action<PortItemViewModel> onShowOnMapToggle, Action<PortItemViewModel> onDelete, Action<PortItemViewModel> onEdit) : ViewModelBase
{
    private readonly PortConfig _config = config;

    public Guid Id => _config.Id;

    public string Name => _config.Name;

    public string TypeName => _config.TypeSettings switch
    {
        AprsIsSettings => "APRS-IS",
        KissSettings => "KISS",
        _ => "Unknown"
    };

    public string StatusText => IsEnabled ? "Running" : "Stopped";

    [ObservableProperty]
    public partial bool IsEnabled { get; set; } = config.IsEnabled;

    [ObservableProperty]
    public partial bool ShowOnMap { get; set; } = config.ShowOnMap;

    partial void OnIsEnabledChanged(bool value)
    {
        OnPropertyChanged(nameof(StatusText));
        onToggle(this);
    }

    partial void OnShowOnMapChanged(bool value)
    {
        onShowOnMapToggle(this);
    }

    public void Delete()
    {
        onDelete(this);
    }

    public void Edit()
    {
        onEdit(this);
    }

    public PortConfig BuildConfig()
    {
        _config.IsEnabled = IsEnabled;
        _config.ShowOnMap = ShowOnMap;
        return _config;
    }
}