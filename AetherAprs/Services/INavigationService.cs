// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.ViewModels;
using System;

namespace AetherAprs.Services;

public interface INavigationService
{
    ViewModelBase? CurrentViewModel { get; }

    event EventHandler<ViewModelBase?>? CurrentViewModelChanged;

    void NavigateTo<TViewModel>() where TViewModel : ViewModelBase;

    /// <summary>
    /// Navigates to an existing view model instance (preserves Initialize state).
    /// </summary>
    void NavigateTo(ViewModelBase viewModel);

    bool CanGoBack { get; }

    void GoBack();

    event EventHandler? RequestAppExit;
}
