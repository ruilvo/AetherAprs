// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later
using CommunityToolkit.Mvvm.ComponentModel;
using AetherAprs.Services;

namespace AetherAprs.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly INavigationService _navigationService;

    [ObservableProperty]
    public partial int SelectedTabIndex { get; set; } = 0;

    public HomeViewModel HomeViewModel { get; }
    public SettingsViewModel SettingsViewModel { get; }

    public MainViewModel(INavigationService navService, HomeViewModel homeViewModel, SettingsViewModel settingsViewModel)
    {
        _navigationService = navService;
        HomeViewModel = homeViewModel;
        SettingsViewModel = settingsViewModel;

        // Subscribe to navigation changes to update tab index
        _navigationService.CurrentViewModelChanged += OnCurrentViewModelChanged;

        // Navigate to home on startup
        _navigationService.NavigateTo<HomeViewModel>();
    }

    partial void OnSelectedTabIndexChanged(int value)
    {
        // When user clicks tabs, update navigation service
        if (value == 0)
        {
            _navigationService.NavigateTo<HomeViewModel>();
        }
        else if (value == 1)
        {
            _navigationService.NavigateTo<SettingsViewModel>();
        }
    }

    private void OnCurrentViewModelChanged(object? sender, ViewModelBase? viewModel)
    {
        // When navigation service changes, update tab index
        if (viewModel is HomeViewModel)
        {
            SelectedTabIndex = 0;
        }
        else if (viewModel is SettingsViewModel)
        {
            SelectedTabIndex = 1;
        }
    }
}
