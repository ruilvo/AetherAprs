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
    public partial ViewModelBase? CurrentContent { get; set; }

    public NavigationBarViewModel NavigationBar { get; }

    public MainViewModel(INavigationService navService, NavigationBarViewModel navigationBarViewModel)
    {
        navigationService = navService;
        NavigationBar = navigationBarViewModel;

        navigationService.CurrentViewModelChanged += OnCurrentViewModelChanged;

        // Navigate to home page on startup
        navigationService.NavigateTo<HomeViewModel>();
    }

    private void OnCurrentViewModelChanged(object? sender, ViewModelBase? viewModel)
    {
        CurrentContent = viewModel;
    }
}
