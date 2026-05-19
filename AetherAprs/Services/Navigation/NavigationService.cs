// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AetherAprs.Services.Navigation;

public partial class NavigationService(INavigationFactory factory) : ObservableObject, INavigationService
{
    private readonly Stack<ObservableObject> _history = new();

    // Main Page Screen Content
    [ObservableProperty]
    public partial ObservableObject? CurrentView { get; set; }

    public bool CanGoBack => _history.Count > 0;

    public void NavigateTo<T>() where T : ObservableObject
    {
        if (CurrentView != null)
        {
            _history.Push(CurrentView);
            OnPropertyChanged(nameof(CanGoBack));
        }

        CurrentView = factory.Create<T>();
    }

    public void GoBack()
    {
        if (_history.TryPop(out var previous))
        {
            CurrentView = previous;
            OnPropertyChanged(nameof(CanGoBack));
        }
    }
}
