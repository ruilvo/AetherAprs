// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;

using AetherAprs.ViewModels;
using AetherAprs.ViewModels.Pages;

using Microsoft.Extensions.DependencyInjection;

namespace AetherAprs.Services.UI;

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
    private readonly Stack<ViewModelBase> _navigationStack = new();

    public ViewModelBase? CurrentViewModel
    {
        get;
        private set
        {
            if (field != value)
            {
                field = value;
                CurrentViewModelChanged?.Invoke(this, field);
            }
        }
    }

    public event EventHandler<ViewModelBase?>? CurrentViewModelChanged;
    public event EventHandler? RequestAppExit;

    public void NavigateTo<TViewModel>() where TViewModel : ViewModelBase => NavigateTo(serviceProvider.GetRequiredService<TViewModel>());

    public void NavigateTo(ViewModelBase viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        if (IsRootTab(viewModel))
        {
            _navigationStack.Clear();
            CurrentViewModel = viewModel;
            return;
        }

        if (CurrentViewModel != null && !ReferenceEquals(CurrentViewModel, viewModel))
        {
            _navigationStack.Push(CurrentViewModel);
        }

        CurrentViewModel = viewModel;
    }

    public bool CanGoBack =>
        _navigationStack.Count > 0
        || (CurrentViewModel != null && !IsRootTab(CurrentViewModel))
        || (CurrentViewModel != null && CurrentViewModel.GetType() != typeof(HomeViewModel));

    public void GoBack()
    {
        if (_navigationStack.Count > 0)
        {
            CurrentViewModel = _navigationStack.Pop();
            return;
        }

        if (CurrentViewModel != null && !IsRootTab(CurrentViewModel))
        {
            CurrentViewModel = serviceProvider.GetRequiredService<HomeViewModel>();
            return;
        }

        if (CurrentViewModel?.GetType() != typeof(HomeViewModel))
        {
            CurrentViewModel = serviceProvider.GetRequiredService<HomeViewModel>();
            return;
        }

        RequestAppExit?.Invoke(this, EventArgs.Empty);
    }

    private static bool IsRootTab(ViewModelBase viewModel) =>
        RootTabTypes.Contains(viewModel.GetType());
}
