// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.ViewModels.Pages;

namespace AetherAprs.Factories.ViewModels;

/// <summary>
/// Factory for creating and initializing PacketDetailsViewModel instances.
/// </summary>
public interface IPacketDetailsViewModelFactory
{
    /// <summary>
    /// Creates a PacketDetailsViewModel initialized with the specified source callsign.
    /// </summary>
    /// <param name="source">The source callsign to load packet details for.</param>
    /// <returns>An initialized PacketDetailsViewModel.</returns>
    PacketDetailsViewModel Create(string source);
}
