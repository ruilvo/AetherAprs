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
    private readonly INavigationFactory _factory;
    private readonly ILoggingService _log;

    [ObservableProperty]
    public partial ObservableObject? CurrentView { get; set; }


    public NavigationService(INavigationFactory factory, ILoggingService loggingService)
    {
        _factory = factory;
        _log = loggingService.ForContext(nameof(NavigationService));
    }

    /// <inheritdoc/>
    public bool CanGoBack => false;

    /// <inheritdoc/>
    public void NavigateTo<T>() where T : ObservableObject
    {
        var targetName = typeof(T).Name;
        var fromName = CurrentView?.GetType().Name ?? "(none)";
        _log.Debug($"NavigateTo {targetName} (from {fromName}).");

        CurrentView = _factory.Create<T>();
    }

    /// <inheritdoc/>
    public void GoBack()
    {
        _log.Debug("GoBack called with empty history; no-op.");
    }

    /// <inheritdoc/>
    public void ClearHistory()
    {
        _log.Debug($"Clearing navigation history os a no-op since history is disabled.");
    }
}
