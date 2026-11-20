// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Models.Aprs.Packets;
using AetherAprs.ViewModels.Pages;

namespace AetherAprs.Factories.ViewModels;

/// <summary>
/// Factory for creating and initializing ObjectEditorViewModel instances.
/// </summary>
public interface IObjectEditorViewModelFactory
{
    /// <summary>
    /// Creates an object editor initialized for a new object.
    /// </summary>
    /// <returns>An ObjectEditorViewModel in add mode.</returns>
    ObjectEditorViewModel Create();

    /// <summary>
    /// Creates an object editor initialized with the given existing object
    /// for editing (or killing).
    /// </summary>
    /// <param name="existing">The object to edit, or null to create a new object.</param>
    /// <returns>An initialized ObjectEditorViewModel.</returns>
    ObjectEditorViewModel Create(ObjectPacket? existing);
}
