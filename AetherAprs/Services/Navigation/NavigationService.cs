// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AetherAprs.Services.Navigation;

/// <summary>
/// Default <see cref="INavigationService"/> implementation with bounded history and disposal support.
/// </summary>
public partial class NavigationService(INavigationFactory factory) : ObservableObject, INavigationService
{
    private const int MaxHistorySize = 50;
    private readonly Stack<ObservableObject> _history = new();

    [ObservableProperty]
    private ObservableObject? _currentView;

    /// <inheritdoc/>
    public bool CanGoBack => _history.Count > 0;

    /// <inheritdoc/>
    public void NavigateTo<T>() where T : ObservableObject
    {
        if (CurrentView != null)
        {
            _history.Push(CurrentView);

            // Limit history size to prevent unbounded memory growth
            if (_history.Count > MaxHistorySize)
            {
                TrimHistory();
            }

            OnPropertyChanged(nameof(CanGoBack));
        }

        CurrentView = factory.Create<T>();
    }

    /// <inheritdoc/>
    public void GoBack()
    {
        if (!_history.TryPop(out var previous))
        {
            return;
        }

        // Dispose the current view if it implements IDisposable
        DisposeViewModel(CurrentView);

        CurrentView = previous;
        OnPropertyChanged(nameof(CanGoBack));
    }

    /// <inheritdoc/>
    public void ClearHistory()
    {
        while (_history.TryPop(out var viewModel))
        {
            DisposeViewModel(viewModel);
        }

        OnPropertyChanged(nameof(CanGoBack));
    }

    private void TrimHistory()
    {
        // Remove the oldest half of the history when limit is exceeded
        var itemsToRemove = _history.Count - (MaxHistorySize / 2);
        if (itemsToRemove <= 0)
        {
            return;
        }

        var tempStack = new Stack<ObservableObject>(_history.Count - itemsToRemove);

        // Keep the most recent items
        for (var i = 0; i < _history.Count - itemsToRemove; i++)
        {
            tempStack.Push(_history.Pop());
        }

        // Dispose the oldest items
        while (_history.TryPop(out var oldViewModel))
        {
            DisposeViewModel(oldViewModel);
        }

        // Restore the kept items
        while (tempStack.TryPop(out var viewModel))
        {
            _history.Push(viewModel);
        }
    }

    private static void DisposeViewModel(ObservableObject? viewModel)
    {
        if (viewModel is IDisposable disposable)
        {
            try
            {
                disposable.Dispose();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error disposing view model: {ex.Message}");
            }
        }
    }
}
