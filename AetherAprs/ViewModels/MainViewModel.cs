// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later
using AetherAprs.Services.Navigation;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AetherAprs.ViewModels;

/// <summary>
/// Root view model that owns the navigation service and acts as the shell for the application.
/// </summary>
public partial class MainViewModel : ViewModelBase
{
    /// <summary>
    /// Gets the navigation service used to switch between pages.
    /// </summary>
    public INavigationService NavService { get; }


    public MainViewModel(INavigationService navService)
    {
        NavService = navService;

        NavService.NavigateTo<HomeViewModel>();
    }
}
