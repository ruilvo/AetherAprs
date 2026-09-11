// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using CommunityToolkit.Mvvm.ComponentModel;
using AetherAprs.Configuration;
using AetherAprs.Models;

namespace AetherAprs.ViewModels;

public partial class PortItemViewModel(PortConfig config, Action<PortItemViewModel> onToggle, Action<PortItemViewModel> onDelete, Action<PortItemViewModel> onEdit) : ViewModelBase
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
    public partial bool IsRx { get; set; } = config.IsRx;

    [ObservableProperty]
    public partial bool IsTx { get; set; } = config.IsTx;

    [ObservableProperty]
    public partial string? SymbolTableCharacter { get; set; } = config.SymbolTableCharacter;

    [ObservableProperty]
    public partial string? SymbolCodeCharacter { get; set; } = config.SymbolCodeCharacter;

    [ObservableProperty]
    public partial DynamicBeaconMode? DynamicBeaconMode { get; set; } = config.DynamicBeaconMode;

    [ObservableProperty]
    public partial string? Server { get; set; } = config.Server;

    [ObservableProperty]
    public partial int ServerPort { get; set; } = config.ServerPort;

    [ObservableProperty]
    public partial string? Passcode { get; set; } = config.Passcode;

    [ObservableProperty]
    public partial string? Filter { get; set; } = config.Filter;

    [ObservableProperty]
    public partial int? Ssid { get; set; } = config.Ssid;

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

    public void Edit()
    {
        onEdit(this);
    }

    public PortConfig ToConfig()
    {
        return new PortConfig
        {
            Id = Id,
            Name = Name,
            Type = Type,
            IsEnabled = IsEnabled,
            IsRx = IsRx,
            IsTx = IsTx,
            SymbolTableCharacter = SymbolTableCharacter,
            SymbolCodeCharacter = SymbolCodeCharacter,
            DynamicBeaconMode = DynamicBeaconMode,
            Server = Server,
            ServerPort = ServerPort,
            Passcode = Passcode,
            Filter = Filter,
            Ssid = Ssid
        };
    }
}
