// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using AetherAprs.Services.UI;

namespace AetherAprs.Tests.Helpers
{
    /// <summary>
    /// Test double that executes posted actions synchronously on the calling thread,
    /// keeping event-handler assertions deterministic without a real UI thread.
    /// </summary>
    public sealed class SynchronousUiDispatcher : IUiDispatcher
    {
        public static SynchronousUiDispatcher Instance { get; } = new();

        public void Post(Action action)
        {
            ArgumentNullException.ThrowIfNull(action);
            action();
        }
    }
}
