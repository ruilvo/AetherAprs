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
using System.Collections.Generic;
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
    private readonly ISessionStatusSource? _sessionStatusSource;
    private readonly ILogger<PortsViewModel> _logger;
    private readonly ILoggerFactory _loggerFactory;

    [ObservableProperty]
    public partial string Title { get; set; } = Localization.Strings.Get("Ports");

    public ObservableCollection<PortItemViewModel> PortItems { get; } = [];
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
        IUiDispatcher? uiDispatcher = null,
        ISessionStatusSource? sessionStatusSource = null)
    {
        _portService = portService;
        _configurationService = configurationService;
        _navigationService = navigationService;
        _addEditPortViewModelFactory = addEditPortViewModelFactory;
        _confirmationDialogService = confirmationDialogService;
        _transientFeedbackService = transientFeedbackService;
        _uiDispatcher = uiDispatcher;
        _sessionStatusSource = sessionStatusSource;
        _logger = logger;
        _loggerFactory = loggerFactory;
        _portService.PortsChanged += OnPortsChanged;

        if (_sessionStatusSource is not null)
        {
            _sessionStatusSource.StateChanged += OnSessionStateChanged;
        }

        LoadPorts();
    }

    private void LoadPorts()
    {
        PortItems.Clear();
        foreach (PortConfig port in _portService.Ports)
        {
            PortItemViewModel item = new(
                port,
                OnTogglePortAsync,
                OnToggleShowOnMapAsync,
                OnDeletePortAsync,
                OnEditPort,
                _loggerFactory.CreateLogger<PortItemViewModel>());
            item.ApplySessionStatus(FindSessionStatus(port.Id));
            PortItems.Add(item);
        }
        OnPropertyChanged(nameof(HasPorts));
    }

    private PortSessionStatus? FindSessionStatus(Guid portId)
    {
        if (_sessionStatusSource is null)
        {
            return null;
        }

        foreach (PortSessionStatus status in _sessionStatusSource.Ports)
        {
            if (status.PortId == portId)
            {
                return status;
            }
        }

        return null;
    }

    private void OnSessionStateChanged(object? sender, SessionStateChangedEventArgs args)
    {
        if (_uiDispatcher is null)
        {
            ApplySessionStatuses(args.Snapshot.Ports);
        }
        else
        {
            _uiDispatcher.Post(() => ApplySessionStatuses(args.Snapshot.Ports));
        }
    }

    private void ApplySessionStatuses(IReadOnlyList<PortSessionStatus> statuses)
    {
        foreach (PortItemViewModel item in PortItems)
        {
            PortSessionStatus? status = null;
            foreach (PortSessionStatus candidate in statuses)
            {
                if (candidate.PortId == item.Id)
                {
                    status = candidate;
                    break;
                }
            }

            item.ApplySessionStatus(status);
        }
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

        if (_sessionStatusSource is not null)
        {
            _sessionStatusSource.StateChanged -= OnSessionStateChanged;
        }
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
        bool confirmed = await _confirmationDialogService.ShowConfirmationAsync(
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
        PortConfig config = item.BuildConfig();
        AddEditPortViewModel vm = _addEditPortViewModelFactory.CreateForEdit(
            _configurationService.Current.Aprs.Callsign,
            GetNextPortNumber(),
            config);
        _navigationService.NavigateTo(vm);
    }

    [RelayCommand]
    public void AddPort()
    {
        _logger.LogInformation("Add port requested");
        AddEditPortViewModel vm = _addEditPortViewModelFactory.CreateForAdd(
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
        int highest = 0;
        foreach (PortConfig port in _portService.Ports)
        {
            int number = DefaultNameNumber(port);
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
        string template = port.TypeSettings is AprsIsSettings
            ? "DefaultAprsIsPortName"
            : port.TypeSettings is AfskSettings
                ? "DefaultAfskPortName"
                : "DefaultKissPortName";

        // Formatting with an empty argument yields the localized prefix
        // ("APRS-IS Port ", "KISS Port ") that the editor prepends to a number.
        string prefix = Localization.Strings.Format(template, string.Empty);
        return port.Name.StartsWith(prefix, StringComparison.Ordinal)
            && int.TryParse(port.Name.AsSpan(prefix.Length), out int number)
                ? number
                : 0;
    }
}
