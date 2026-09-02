// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later
using CommunityToolkit.Mvvm.Input;
using AetherAprs.Services;

namespace AetherAprs.ViewModels;

public partial class NavigationBarViewModel(INavigationService navService) : ViewModelBase
{
    [RelayCommand]
    private void NavigateToHome()
    {
        navService.NavigateTo<HomeViewModel>();
    }

    [RelayCommand]
    private void NavigateToSettings()
    {
        navService.NavigateTo<SettingsViewModel>();
    }
}
