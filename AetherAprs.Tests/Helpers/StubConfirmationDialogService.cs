// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Services.UI;
using System.Threading.Tasks;

namespace AetherAprs.Tests.Helpers;

/// <summary>
/// Stub confirmation dialog service that never blocks: always confirms.
/// </summary>
public sealed class StubConfirmationDialogService : IConfirmationDialogService
{
    public static StubConfirmationDialogService Instance { get; } = new();

    public bool Confirmed { get; set; } = true;
    public int CallCount { get; private set; }

    public Task<bool> ShowConfirmationAsync(string title, string message)
    {
        CallCount++;
        return Task.FromResult(Confirmed);
    }
}
