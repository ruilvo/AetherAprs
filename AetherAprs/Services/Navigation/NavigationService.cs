// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Services.Logging;
using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AetherAprs.Services.Navigation;

/// <summary>
/// Default <see cref="INavigationService"/> implementation with bounded history and disposal support.
/// </summary>
public partial class NavigationService : ObservableObject, INavigationService
{
    private const int MaxHistorySize = 50;

    private readonly INavigationFactory _factory;
    private readonly ILoggingService _log;
    private readonly Stack<ObservableObject> _history = new();

    [ObservableProperty]
    private ObservableObject? _currentView;

    public NavigationService(INavigationFactory factory, ILoggingService loggingService)
    {
        _factory = factory;
        _log = loggingService.ForContext(nameof(NavigationService));
    }

    /// <inheritdoc/>
    public bool CanGoBack => _history.Count > 0;

    /// <inheritdoc/>
    public void NavigateTo<T>() where T : ObservableObject
    {
        var targetName = typeof(T).Name;
        var fromName = CurrentView?.GetType().Name ?? "(none)";
        _log.Debug($"NavigateTo {targetName} (from {fromName})");

        if (CurrentView != null)
        {
            _history.Push(CurrentView);

            if (_history.Count > MaxHistorySize)
            {
                _log.Warn($"Navigation history exceeded {MaxHistorySize}; trimming");
                TrimHistory();
            }

            OnPropertyChanged(nameof(CanGoBack));
        }

        CurrentView = _factory.Create<T>();
    }

    /// <inheritdoc/>
    public void GoBack()
    {
        if (!_history.TryPop(out var previous))
        {
            _log.Debug("GoBack called with empty history; no-op");
            return;
        }

        _log.Debug($"GoBack to {previous.GetType().Name} (from {CurrentView?.GetType().Name ?? "(none)"})");
        DisposeViewModel(CurrentView);

        CurrentView = previous;
        OnPropertyChanged(nameof(CanGoBack));
    }

    /// <inheritdoc/>
    public void ClearHistory()
    {
        _log.Debug($"Clearing navigation history ({_history.Count} entries)");
        while (_history.TryPop(out var viewModel))
        {
            DisposeViewModel(viewModel);
        }

        OnPropertyChanged(nameof(CanGoBack));
    }

    private void TrimHistory()
    {
        var itemsToRemove = _history.Count - (MaxHistorySize / 2);
        if (itemsToRemove <= 0)
        {
            return;
        }

        var tempStack = new Stack<ObservableObject>(_history.Count - itemsToRemove);

        for (var i = 0; i < _history.Count - itemsToRemove; i++)
        {
            tempStack.Push(_history.Pop());
        }

        while (_history.TryPop(out var oldViewModel))
        {
            DisposeViewModel(oldViewModel);
        }

        while (tempStack.TryPop(out var viewModel))
        {
            _history.Push(viewModel);
        }
    }

    private void DisposeViewModel(ObservableObject? viewModel)
    {
        if (viewModel is IDisposable disposable)
        {
            try
            {
                disposable.Dispose();
            }
            catch (Exception ex)
            {
                _log.Error($"Disposing {viewModel.GetType().Name} threw: {ex.Message}");
            }
        }
    }
}
