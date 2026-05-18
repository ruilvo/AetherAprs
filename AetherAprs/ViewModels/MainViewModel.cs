// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later
using AetherAprs.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AetherAprs.ViewModels;

public partial class MainViewModel : ViewModelWithPopupBase
{
    public INavigationService NavService { get; }
    
    public MainViewModel(INavigationService navService)
    {
        NavService = navService;

        // FUTURE STARTUP ROUTE: 
        // NavService.NavigateTo<YourInitialViewModel>();
    }
}

