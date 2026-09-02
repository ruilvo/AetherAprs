// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AetherAprs.Services;

namespace AetherAprs.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly INavigationService navigationService;

    [ObservableProperty]
    public partial ViewModelBase? CurrentContent { get; set; }


    public MainViewModel(INavigationService navService)
    {
        navigationService = navService;

        navigationService.CurrentViewModelChanged += OnCurrentViewModelChanged;

        // Navigate to home page on startup
        navigationService.NavigateTo<HomeViewModel>();
    }

    private void OnCurrentViewModelChanged(object? sender, ViewModelBase? viewModel)
    {
        CurrentContent = viewModel;
    }

    [RelayCommand]
    private void NavigateToHome()
    {
        navigationService.NavigateTo<HomeViewModel>();
    }

    [RelayCommand]
    private void NavigateToSettings()
    {
        navigationService.NavigateTo<SettingsViewModel>();
    }

    [RelayCommand]
    private void GoBack()
    {
        navigationService.GoBack();
    }
}
