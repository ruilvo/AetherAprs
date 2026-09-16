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
using Microsoft.Extensions.Logging;

namespace AetherAprs.ViewModels;

public partial class PortsViewModel : ViewModelBase
{
    private readonly IPortService _portService;
    private readonly IConfigurationService _configurationService;
    private readonly INavigationService _navigationService;
    private readonly ILogger<PortsViewModel> _logger;

    [ObservableProperty]
    public partial string Title { get; set; } = Localization.Strings.Get("Ports");

    public ObservableCollection<PortItemViewModel> PortItems { get; } = new();

    public PortsViewModel(
        IPortService portService,
        IConfigurationService configurationService,
        INavigationService navigationService,
        ILogger<PortsViewModel> logger)
    {
        _portService = portService;
        _configurationService = configurationService;
        _navigationService = navigationService;
        _logger = logger;
        _portService.PortsChanged += OnPortsChanged;
        LoadPorts();
    }

    private void LoadPorts()
    {
        PortItems.Clear();
        foreach (var port in _portService.Ports)
        {
            var item = new PortItemViewModel(port, OnTogglePort, OnToggleShowOnMap, OnDeletePort, OnEditPort);
            PortItems.Add(item);
        }
    }

    private void OnPortsChanged(object? sender, EventArgs e)
    {
        LoadPorts();
    }

    private void OnTogglePort(PortItemViewModel item)
    {
        _logger.LogInformation("Toggle port {PortName} ({PortId}) Enabled={IsEnabled}", item.Name, item.Id, item.IsEnabled);
        _ = _portService.SetPortEnabledAsync(item.Id, item.IsEnabled);
    }

    private void OnToggleShowOnMap(PortItemViewModel item)
    {
        _logger.LogInformation("Toggle ShowOnMap for port {PortName} ({PortId}) ShowOnMap={ShowOnMap}", item.Name, item.Id, item.ShowOnMap);
        _ = _portService.SetPortShowOnMapAsync(item.Id, item.ShowOnMap);
    }

    private void OnDeletePort(PortItemViewModel item)
    {
        _logger.LogInformation("Remove port {PortName} ({PortId})", item.Name, item.Id);
        _ = _portService.RemovePortAsync(item.Id);
    }

    public void OnEditPort(PortItemViewModel item)
    {
        _logger.LogInformation("Edit port {PortName} ({PortId})", item.Name, item.Id);
        var vm = App.GetService<Pages.AddEditPortViewModel>();
        var config = item.BuildConfig();
        vm.Initialize(
            _configurationService.Settings.Aprs.Callsign,
            GetNextPortNumber(),
            _configurationService.Settings.Aprs.DefaultSymbolTableCharacter,
            _configurationService.Settings.Aprs.DefaultSymbolCodeCharacter,
            config);
        _navigationService.NavigateTo(vm);
    }

    [RelayCommand]
    public void AddPort()
    {
        _logger.LogInformation("Add port requested");
        var vm = App.GetService<Pages.AddEditPortViewModel>();
        vm.Initialize(
            _configurationService.Settings.Aprs.Callsign,
            GetNextPortNumber(),
            _configurationService.Settings.Aprs.DefaultSymbolTableCharacter,
            _configurationService.Settings.Aprs.DefaultSymbolCodeCharacter);
        _navigationService.NavigateTo(vm);
    }

    [RelayCommand]
    private void DeletePort(PortItemViewModel item)
    {
        _logger.LogInformation("Remove port {PortName} ({PortId})", item.Name, item.Id);
        _ = _portService.RemovePortAsync(item.Id);
    }

    public int GetNextPortNumber()
    {
        var aprsIsPorts = _portService.Ports
            .Where(p => p.TypeSettings is AprsIsSettings)
            .ToList();
        return aprsIsPorts.Count + 1;
    }
}
