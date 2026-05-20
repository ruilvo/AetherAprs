// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using CommunityToolkit.Mvvm.ComponentModel;

namespace AetherAprs.Services.Navigation;

/// <summary>
/// Provides navigation between view models with back-stack support.
/// </summary>
public interface INavigationService
{
    /// <summary>
    /// Gets the currently displayed view model.
    /// </summary>
    ObservableObject? CurrentView { get; }

    /// <summary>
    /// Gets a value indicating whether the navigation history contains entries to navigate back to.
    /// </summary>
    bool CanGoBack { get; }

    /// <summary>
    /// Navigates to a new view model of type <typeparamref name="T"/>, pushing the current view onto the back stack.
    /// </summary>
    void NavigateTo<T>() where T : ObservableObject;

    /// <summary>
    /// Navigates back to the previous view model in the history stack, if available.
    /// </summary>
    void GoBack();

    /// <summary>
    /// Clears the entire navigation history.
    /// </summary>
    void ClearHistory();
}