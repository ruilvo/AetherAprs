// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Configuration;
using AetherAprs.ViewModels.Pages;

namespace AetherAprs.Factories;

/// <summary>
/// Factory for creating AddEditPortViewModel instances with context-specific initialization.
/// </summary>
public interface IAddEditPortViewModelFactory
{
    /// <summary>
    /// Creates a new AddEditPortViewModel for adding a new port.
    /// </summary>
    /// <param name="callsign">The callsign to use for the new port.</param>
    /// <param name="portNumber">The port number to suggest.</param>
    /// <returns>An initialized AddEditPortViewModel.</returns>
    AddEditPortViewModel CreateForAdd(string callsign, int portNumber);

    /// <summary>
    /// Creates a new AddEditPortViewModel for editing an existing port.
    /// </summary>
    /// <param name="callsign">The callsign to use.</param>
    /// <param name="portNumber">The port number to suggest.</param>
    /// <param name="existingConfig">The existing port configuration to edit.</param>
    /// <returns>An initialized AddEditPortViewModel.</returns>
    AddEditPortViewModel CreateForEdit(string callsign, int portNumber, PortConfig existingConfig);
}
