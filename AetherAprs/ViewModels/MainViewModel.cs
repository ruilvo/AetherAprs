// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later
using AetherAprs.Services.Navigation;

namespace AetherAprs.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    public INavigationService NavService { get; }

    // Required for the Avalonia Designer
    public MainViewModel()
    {
        NavService = null!;
    }

    public MainViewModel(INavigationService navService)
    {
        NavService = navService;

        NavService.NavigateTo<HomeViewModel>();
    }
}

