// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using CommunityToolkit.Mvvm.ComponentModel;
using AetherAprs.Configuration;

namespace AetherAprs.ViewModels;

public partial class PortItemViewModel(PortConfig config, Action<PortItemViewModel> onToggle, Action<PortItemViewModel> onDelete) : ViewModelBase
{

    [ObservableProperty]
    public partial Guid Id { get; set; } = config.Id;

    [ObservableProperty]
    public partial string Name { get; set; } = config.Name;

    [ObservableProperty]
    public partial PortType Type { get; set; } = config.Type;

    [ObservableProperty]
    public partial bool IsEnabled { get; set; } = config.IsEnabled;

    [ObservableProperty]
    public partial string StatusText { get; set; } = config.IsEnabled ? "Running" : "Stopped";

    partial void OnIsEnabledChanged(bool value)
    {
        StatusText = value ? "Running" : "Stopped";
        onToggle(this);
    }

    public void Delete()
    {
        onDelete(this);
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