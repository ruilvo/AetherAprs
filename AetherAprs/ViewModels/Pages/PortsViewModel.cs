// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Configuration;
using AetherAprs.Factories.ViewModels;
using AetherAprs.Services.Configuration;
using AetherAprs.Services.Ports;
using AetherAprs.Services.UI;
using AetherAprs.ViewModels.Components;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace AetherAprs.ViewModels.Pages;

public partial class PortsViewModel : ViewModelBase, IDisposable
{
    private readonly IPortService _portService;
    private readonly IConfigurationService _configurationService;
    private readonly INavigationService _navigationService;
    private readonly IAddEditPortViewModelFactory _addEditPortViewModelFactory;
    private readonly IConfirmationDialogService _confirmationDialogService;
    private readonly ITransientFeedbackService? _transientFeedbackService;
    private readonly IUiDispatcher? _uiDispatcher;
    private readonly ILogger<PortsViewModel> _logger;
    private readonly ILoggerFactory _loggerFactory;

    [ObservableProperty]
    public partial string Title { get; set; } = Localization.Strings.Get("Ports");

    public ObservableCollection<PortItemViewModel> PortItems { get; } = new();
    public bool HasPorts => PortItems.Count > 0;

    public PortsViewModel(
        IPortService portService,
        IConfigurationService configurationService,
        INavigationService navigationService,
        IAddEditPortViewModelFactory addEditPortViewModelFactory,
        IConfirmationDialogService confirmationDialogService,
        ILogger<PortsViewModel> logger,
        ILoggerFactory loggerFactory,
        ITransientFeedbackService? transientFeedbackService = null,
        IUiDispatcher? uiDispatcher = null)
    {
        _portService = portService;
        _configurationService = configurationService;
        _navigationService = navigationService;
        _addEditPortViewModelFactory = addEditPortViewModelFactory;
        _confirmationDialogService = confirmationDialogService;
        _transientFeedbackService = transientFeedbackService;
        _uiDispatcher = uiDispatcher;
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
        OnPropertyChanged(nameof(HasPorts));
    }

    private void OnPortsChanged(object? sender, EventArgs e)
    {
        if (_uiDispatcher is null)
        {
            LoadPorts();
        }
        else
        {
            _uiDispatcher.Post(LoadPorts);
        }
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _portService.PortsChanged -= OnPortsChanged;
    }

    private async Task OnTogglePortAsync(PortItemViewModel item)
    {
        _logger.LogInformation("Toggle port {PortName} ({PortId}) Enabled={IsEnabled}", item.Name, item.Id, item.IsEnabled);
        await _portService.SetPortEnabledAsync(item.Id, item.IsEnabled);
        _transientFeedbackService?.Show(Localization.Strings.Format(
            item.IsEnabled ? "PortStartedFeedback" : "PortStoppedFeedback", item.Name));
    }

    private async Task OnToggleShowOnMapAsync(PortItemViewModel item)
    {
        _logger.LogInformation("Toggle ShowOnMap for port {PortName} ({PortId}) ShowOnMap={ShowOnMap}", item.Name, item.Id, item.ShowOnMap);
        await _portService.SetPortShowOnMapAsync(item.Id, item.ShowOnMap);
    }

    private async Task OnDeletePortAsync(PortItemViewModel item)
    {
        var confirmed = await _confirmationDialogService.ShowConfirmationAsync(
            Localization.Strings.Get("DeletePort"),
            Localization.Strings.Format("DeletePortConfirmation", item.Name));

        if (!confirmed)
        {
            return;
        }

        _logger.LogInformation("Remove port {PortName} ({PortId})", item.Name, item.Id);
        await _portService.RemovePortAsync(item.Id);
    }

    public void OnEditPort(PortItemViewModel item)
    {
        _logger.LogInformation("Edit port {PortName} ({PortId})", item.Name, item.Id);
        var config = item.BuildConfig();
        var vm = _addEditPortViewModelFactory.CreateForEdit(
            _configurationService.Current.Aprs.Callsign,
            GetNextPortNumber(),
            config);
        _navigationService.NavigateTo(vm);
    }

    [RelayCommand]
    public void AddPort()
    {
        _logger.LogInformation("Add port requested");
        var vm = _addEditPortViewModelFactory.CreateForAdd(
            _configurationService.Current.Aprs.Callsign,
            GetNextPortNumber());
        _navigationService.NavigateTo(vm);
    }

    public int GetNextPortNumber()
    {
        // Default names embed the number, and that number must stay unique across
        // port types: counting APRS-IS ports alone reused a KISS number when two
        // KISS ports existed and ignored the numbers a deleted port had already
        // used.
        var highest = 0;
        foreach (var port in _portService.Ports)
        {
            var number = DefaultNameNumber(port);
            if (number > highest)
            {
                highest = number;
            }
        }

        // A custom name carries no number, so the configured port count is the
        // best lower bound the editor can offer.
        return (highest == 0 ? _portService.Ports.Count : highest) + 1;
    }

    private static int DefaultNameNumber(PortConfig port)
    {
        var template = port.TypeSettings is AprsIsSettings
            ? "DefaultAprsIsPortName"
            : "DefaultKissPortName";

        // Formatting with an empty argument yields the localized prefix
        // ("APRS-IS Port ", "KISS Port ") that the editor prepends to a number.
        var prefix = Localization.Strings.Format(template, string.Empty);
        return port.Name.StartsWith(prefix, StringComparison.Ordinal)
            && int.TryParse(port.Name.AsSpan(prefix.Length), out var number)
                ? number
                : 0;
    }
}
