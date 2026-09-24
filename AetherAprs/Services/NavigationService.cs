// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using AetherAprs.ViewModels;
using AetherAprs.ViewModels.Pages;

namespace AetherAprs.Services;

public class NavigationService(IServiceProvider serviceProvider) : INavigationService
{
    private static readonly HashSet<Type> RootTabTypes =
    [
        typeof(HomeViewModel),
        typeof(PortsViewModel),
        typeof(MessagesViewModel),
        typeof(PacketsViewModel),
        typeof(SettingsViewModel)
    ];

    private ViewModelBase? _currentViewModel;
    private readonly Stack<ViewModelBase> _navigationStack = new();

    public ViewModelBase? CurrentViewModel
    {
        get => _currentViewModel;
        private set
        {
            if (_currentViewModel != value)
            {
                _currentViewModel = value;
                CurrentViewModelChanged?.Invoke(this, _currentViewModel);
            }
        }
    }

    public event EventHandler<ViewModelBase?>? CurrentViewModelChanged;
    public event EventHandler? RequestAppExit;

    public void NavigateTo<TViewModel>() where TViewModel : ViewModelBase
    {
        NavigateTo(serviceProvider.GetRequiredService<TViewModel>());
    }

    public void NavigateTo(ViewModelBase viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        if (IsRootTab(viewModel))
        {
            _navigationStack.Clear();
            CurrentViewModel = viewModel;
            return;
        }

        if (_currentViewModel != null && !ReferenceEquals(_currentViewModel, viewModel))
        {
            _navigationStack.Push(_currentViewModel);
        }

        CurrentViewModel = viewModel;
    }

    public bool CanGoBack =>
        _navigationStack.Count > 0
        || (_currentViewModel != null && !IsRootTab(_currentViewModel))
        || (_currentViewModel != null && _currentViewModel.GetType() != typeof(HomeViewModel));

    public void GoBack()
    {
        if (_navigationStack.Count > 0)
        {
            CurrentViewModel = _navigationStack.Pop();
            return;
        }

        if (_currentViewModel != null && !IsRootTab(_currentViewModel))
        {
            CurrentViewModel = serviceProvider.GetRequiredService<HomeViewModel>();
            return;
        }

        if (_currentViewModel?.GetType() != typeof(HomeViewModel))
        {
            CurrentViewModel = serviceProvider.GetRequiredService<HomeViewModel>();
            return;
        }

        RequestAppExit?.Invoke(this, EventArgs.Empty);
    }

    private static bool IsRootTab(ViewModelBase viewModel) =>
        RootTabTypes.Contains(viewModel.GetType());
}
