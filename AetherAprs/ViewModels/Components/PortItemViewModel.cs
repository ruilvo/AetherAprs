// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using CommunityToolkit.Mvvm.ComponentModel;
using AetherAprs.Configuration;

namespace AetherAprs.ViewModels;

public partial class PortItemViewModel : ViewModelBase
{
    private readonly Action<PortItemViewModel> _onToggle;
    private readonly Action<PortItemViewModel> _onDelete;

    [ObservableProperty]
    public partial Guid Id { get; set; }

    [ObservableProperty]
    public partial string Name { get; set; } = string.Empty;

    [ObservableProperty]
    public partial PortType Type { get; set; }

    [ObservableProperty]
    public partial bool IsEnabled { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; } = "Idle";

    public PortItemViewModel(PortConfig config, Action<PortItemViewModel> onToggle, Action<PortItemViewModel> onDelete)
    {
        Id = config.Id;
        Name = config.Name;
        Type = config.Type;
        IsEnabled = config.IsEnabled;
        _onToggle = onToggle;
        _onDelete = onDelete;
    }

    partial void OnIsEnabledChanged(bool value)
    {
        StatusText = value ? "Running" : "Stopped";
        _onToggle(this);
    }

    public void Delete()
    {
        _onDelete(this);
    }

    public PortConfig ToConfig()
    {
        return new PortConfig
        {
            Id = Id,
            Name = Name,
            Type = Type,
            IsEnabled = IsEnabled
        };
    }
}