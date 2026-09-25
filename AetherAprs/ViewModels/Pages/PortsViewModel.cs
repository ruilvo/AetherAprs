// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Configuration;
using AetherAprs.Factories;
using AetherAprs.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace AetherAprs.ViewModels;

public partial class PortsViewModel : ViewModelBase
{
    private readonly IPortService _portService;
    private readonly IConfigurationService _configurationService;
    private readonly INavigationService _navigationService;
    private readonly IAddEditPortViewModelFactory _addEditPortViewModelFactory;
    private readonly ILogger<PortsViewModel> _logger;
    private readonly ILoggerFactory _loggerFactory;

    [ObservableProperty]
    public partial string Title { get; set; } = Localization.Strings.Get("Ports");

    public ObservableCollection<PortItemViewModel> PortItems { get; } = new();

    public PortsViewModel(
        IPortService portService,
        IConfigurationService configurationService,
        INavigationService navigationService,
        IAddEditPortViewModelFactory addEditPortViewModelFactory,
        ILogger<PortsViewModel> logger,
        ILoggerFactory loggerFactory)
    {
        _portService = portService;
        _configurationService = configurationService;
        _navigationService = navigationService;
        _addEditPortViewModelFactory = addEditPortViewModelFactory;
        _logger = logger;
        _loggerFactory = loggerFactory;
        _portService.PortsChanged += OnPortsChanged;
        LoadPorts();
    }

    private void LoadPorts()
    {
        PortItems.Clear();
        foreach (var port in _portService.Ports)
        {
            var item = new PortItemViewModel(
                port, 
                OnTogglePortAsync, 
                OnToggleShowOnMapAsync, 
                OnDeletePortAsync, 
                OnEditPort,
                _loggerFactory.CreateLogger<PortItemViewModel>());
            PortItems.Add(item);
        }
    }

    private void OnPortsChanged(object? sender, EventArgs e)
    {
        LoadPorts();
    }

    private async Task OnTogglePortAsync(PortItemViewModel item)
    {
        _logger.LogInformation("Toggle port {PortName} ({PortId}) Enabled={IsEnabled}", item.Name, item.Id, item.IsEnabled);
        await _portService.SetPortEnabledAsync(item.Id, item.IsEnabled);
    }

    private async Task OnToggleShowOnMapAsync(PortItemViewModel item)
    {
        _logger.LogInformation("Toggle ShowOnMap for port {PortName} ({PortId}) ShowOnMap={ShowOnMap}", item.Name, item.Id, item.ShowOnMap);
        await _portService.SetPortShowOnMapAsync(item.Id, item.ShowOnMap);
    }

    private async Task OnDeletePortAsync(PortItemViewModel item)
    {
        _logger.LogInformation("Remove port {PortName} ({PortId})", item.Name, item.Id);
        await _portService.RemovePortAsync(item.Id);
    }

    public void OnEditPort(PortItemViewModel item)
    {
        _logger.LogInformation("Edit port {PortName} ({PortId})", item.Name, item.Id);
        var config = item.BuildConfig();
        var vm = _addEditPortViewModelFactory.CreateForEdit(
            _configurationService.Settings.Aprs.Callsign,
            GetNextPortNumber(),
            config);
        _navigationService.NavigateTo(vm);
    }

    [RelayCommand]
    public void AddPort()
    {
        _logger.LogInformation("Add port requested");
        var vm = _addEditPortViewModelFactory.CreateForAdd(
            _configurationService.Settings.Aprs.Callsign,
            GetNextPortNumber());
        _navigationService.NavigateTo(vm);
    }

    public int GetNextPortNumber()
    {
        var aprsIsPorts = _portService.Ports
            .Where(p => p.TypeSettings is AprsIsSettings)
            .ToList();
        return aprsIsPorts.Count + 1;
    }
}
