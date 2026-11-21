// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.ViewModels.Pages;

namespace AetherAprs.Factories.ViewModels;

/// <summary>
/// Factory for creating and initializing ObjectDetailsViewModel instances.
/// </summary>
public interface IObjectDetailsViewModelFactory
{
    /// <summary>
    /// Creates an ObjectDetailsViewModel initialized with the specified object or
    /// item name.
    /// </summary>
    /// <param name="name">The object or item name to load details for.</param>
    /// <returns>An initialized ObjectDetailsViewModel.</returns>
    ObjectDetailsViewModel Create(string name);
}
