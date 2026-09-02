// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later
using CommunityToolkit.Mvvm.ComponentModel;
using AetherAprs.Services;

namespace AetherAprs.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly INavigationService navigationService;

    [ObservableProperty]
    private int selectedTabIndex = 0;

    public HomeViewModel HomeViewModel { get; }
    public SettingsViewModel SettingsViewModel { get; }

    public MainViewModel(INavigationService navService, HomeViewModel homeViewModel, SettingsViewModel settingsViewModel)
    {
        navigationService = navService;
        HomeViewModel = homeViewModel;
        SettingsViewModel = settingsViewModel;

        // Subscribe to navigation changes to update tab index
        navigationService.CurrentViewModelChanged += OnCurrentViewModelChanged;

        // Navigate to home on startup
        navigationService.NavigateTo<HomeViewModel>();
    }

    partial void OnSelectedTabIndexChanged(int value)
    {
        // When user clicks tabs, update navigation service
        if (value == 0)
        {
            navigationService.NavigateTo<HomeViewModel>();
        }
        else if (value == 1)
        {
            navigationService.NavigateTo<SettingsViewModel>();
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
