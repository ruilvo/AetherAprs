// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using CommunityToolkit.Mvvm.ComponentModel;

namespace AetherAprs.Services.Navigation;

/// <summary>
/// Factory abstraction for creating view model instances via the DI container.
/// </summary>
public interface INavigationFactory
{
    /// <summary>
    /// Creates a new instance of the specified view model type.
    /// </summary>
    /// <typeparam name="T">The view model type to create.</typeparam>
    T Create<T>() where T : ObservableObject;
}
