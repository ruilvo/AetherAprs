// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using Avalonia;
using System;

namespace AetherAprs.Services.UI;

/// <summary>
/// Service that manages keyboard insets and provides padding adjustments
/// when the on-screen keyboard appears.
/// </summary>
public interface IKeyboardInsetsService
{
    /// <summary>
    /// Gets the current keyboard insets (bottom padding needed to avoid keyboard).
    /// </summary>
    Thickness CurrentInsets { get; }

    /// <summary>
    /// Event raised when keyboard insets change.
    /// </summary>
    event EventHandler<Thickness>? InsetsChanged;

    /// <summary>
    /// Initializes keyboard tracking for the specified TopLevel.
    /// </summary>
    void Initialize(Avalonia.Controls.TopLevel topLevel);
}
