// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using AetherAprs.ViewModels;

namespace AetherAprs.Services;

public class NavigationService(IServiceProvider serviceProvider) : INavigationService
{
    private ViewModelBase? _currentViewModel;
    private readonly Stack<Type> _navigationStack = new();
    private static readonly Type _homeViewModelType = typeof(HomeViewModel);

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
        var viewModelType = typeof(TViewModel);

        // Clear stack if navigating to home
        if (viewModelType == _homeViewModelType)
        {
            _navigationStack.Clear();
        }
        else
        {
            // Add current page to stack before navigating away
            if (_currentViewModel != null && _currentViewModel.GetType() != viewModelType)
            {
                _navigationStack.Push(_currentViewModel.GetType());
            }
        }

        var viewModel = serviceProvider.GetRequiredService<TViewModel>();
        CurrentViewModel = viewModel;
    }

    public bool CanGoBack => _navigationStack.Count > 0 || (_currentViewModel?.GetType() != _homeViewModelType);

    public void GoBack()
    {
        if (_navigationStack.Count > 0)
        {
            // Navigate to previous page
            var previousType = _navigationStack.Pop();
            var viewModel = (ViewModelBase)serviceProvider.GetRequiredService(previousType);
            CurrentViewModel = viewModel;
        }
        else if (_currentViewModel?.GetType() != _homeViewModelType)
        {
            // Not on home page and no history - go to home
            var homeViewModel = serviceProvider.GetRequiredService<HomeViewModel>();
            CurrentViewModel = homeViewModel;
        }
        else
        {
            // On home page with no history - request app exit
            RequestAppExit?.Invoke(this, EventArgs.Empty);
        }
    }
}
