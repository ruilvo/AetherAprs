// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Threading.Tasks;

namespace AetherAprs.Services.UI;

/// <summary>
/// Shows modal confirmation dialogs and reports the user's choice.
/// </summary>
public interface IConfirmationDialogService
{
    /// <summary>
    /// Shows a modal confirmation dialog with the given title and message.
    /// Returns true when the user confirms, false when cancelled.
    /// </summary>
    Task<bool> ShowConfirmationAsync(string title, string message);
}
