// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AetherAprs.Configuration;
using AetherAprs.Services;

namespace AetherAprs.ViewModels;

public partial class PortsViewModel : ViewModelBase
{
    private readonly IPortService _portService;
    private readonly IConfigurationService _configurationService;

    [ObservableProperty]
    public partial string Title { get; set; } = "Ports";

    public ObservableCollection<PortItemViewModel> PortItems { get; } = new();

    public PortsViewModel(
        IPortService portService,
        IConfigurationService configurationService)
    {
        _portService = portService;
        _configurationService = configurationService;
        _portService.PortsChanged += OnPortsChanged;
        LoadPorts();
    }

    private void LoadPorts()
    {
        PortItems.Clear();
        foreach (var port in _portService.Ports)
        {
            var item = new PortItemViewModel(port, OnTogglePort, OnDeletePort);
            PortItems.Add(item);
        }
    }

    private void OnPortsChanged(object? sender, EventArgs e)
    {
        LoadPorts();
    }

    private void OnTogglePort(PortItemViewModel item)
    {
        _ = _portService.SetPortEnabledAsync(item.Id, item.IsEnabled);
    }

    private void OnDeletePort(PortItemViewModel item)
    {
        _ = _portService.RemovePortAsync(item.Id);
    }

    [RelayCommand]
    private void DeletePort(PortItemViewModel item)
    {
        _ = _portService.RemovePortAsync(item.Id);
    }

    [RelayCommand]
    private void AddPort(PortConfig config)
    {
        _ = _portService.AddPortAsync(config);
    }

    public int GetNextPortNumber()
    {
        var aprsIsPorts = _portService.Ports
            .Where(p => p.Type == PortType.AprsIs)
            .ToList();
        return aprsIsPorts.Count + 1;
    }
}