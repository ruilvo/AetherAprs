// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using AetherAprs.Services;

namespace AetherAprs.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly INavigationService _navigationService;
    private static readonly Dictionary<int, Type> TabIndexToViewModelType = new()
    {
        [0] = typeof(HomeViewModel),
        [1] = typeof(PortsViewModel),
        [2] = typeof(SettingsViewModel)
    };

    private static readonly Dictionary<Type, int> ViewModelTypeToTabIndex = new()
    {
        [typeof(HomeViewModel)] = 0,
        [typeof(PortsViewModel)] = 1,
        [typeof(SettingsViewModel)] = 2
    };

    [ObservableProperty]
    public partial int SelectedTabIndex { get; set; } = 0;

    public HomeViewModel HomeViewModel { get; }
    public PortsViewModel PortsViewModel { get; }
    public SettingsViewModel SettingsViewModel { get; }

    public MainViewModel(
        INavigationService navService,
        HomeViewModel homeViewModel,
        PortsViewModel portsViewModel,
        SettingsViewModel settingsViewModel)
    {
        _navigationService = navService;
        HomeViewModel = homeViewModel;
        PortsViewModel = portsViewModel;
        SettingsViewModel = settingsViewModel;

        // Subscribe to navigation changes to update tab index
        _navigationService.CurrentViewModelChanged += OnCurrentViewModelChanged;

        // Navigate to home on startup
        _navigationService.NavigateTo<HomeViewModel>();
    }

    partial void OnSelectedTabIndexChanged(int value)
    {
        // When user clicks tabs, update navigation service
        if (TabIndexToViewModelType.TryGetValue(value, out var viewModelType))
        {
            if (viewModelType == typeof(HomeViewModel))
                _navigationService.NavigateTo<HomeViewModel>();
            else if (viewModelType == typeof(PortsViewModel))
                _navigationService.NavigateTo<PortsViewModel>();
            else if (viewModelType == typeof(SettingsViewModel))
                _navigationService.NavigateTo<SettingsViewModel>();
        }
    }

    private void OnCurrentViewModelChanged(object? sender, ViewModelBase? viewModel)
    {
        // When navigation service changes, update tab index
        if (viewModel != null && ViewModelTypeToTabIndex.TryGetValue(viewModel.GetType(), out var tabIndex))
        {
            SelectedTabIndex = tabIndex;
        }
    }
}