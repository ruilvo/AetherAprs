// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using AetherAprs.ViewModels;

namespace AetherAprs.Services;

public interface INavigationService
{
    ViewModelBase? CurrentViewModel { get; }
    
    event EventHandler<ViewModelBase?>? CurrentViewModelChanged;
    
    void NavigateTo<TViewModel>() where TViewModel : ViewModelBase;
    
    bool CanGoBack { get; }
    
    void GoBack();
    
    event EventHandler? RequestAppExit;
}
