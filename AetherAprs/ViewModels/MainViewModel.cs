// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later
using AetherAprs.Services.Navigation;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AetherAprs.ViewModels;

/// <summary>
/// Root view model that owns the navigation service and acts as the shell for the application.
/// </summary>
public partial class MainViewModel : ViewModelBase
{
    public INavigationService NavService { get; }

    [ObservableProperty]
    public partial bool IsDrawerOpen { get; set; }

    public MainViewModel(INavigationService navService)
    {
        NavService = navService;

        NavService.NavigateTo<HomeViewModel>();
    }

    [RelayCommand]
    private void NavigateHome()
    {
        NavService.NavigateTo<HomeViewModel>();
        IsDrawerOpen = false;
    }

    [RelayCommand]
    private void NavigateSettings()
    {
        NavService.NavigateTo<SettingsViewModel>();
        IsDrawerOpen = false;
    }
}
