// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using CommunityToolkit.Mvvm.ComponentModel;

namespace AetherAprs.Services;

public interface INavigationService
{
    // Screen Navigation Boundaries
    ObservableObject? CurrentView { get; }
    bool CanGoBack { get; }
    void NavigateTo<T>() where T : ObservableObject;
    void GoBack();
}