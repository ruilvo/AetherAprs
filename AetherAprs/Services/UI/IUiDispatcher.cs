// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;

namespace AetherAprs.Services.UI
{
    /// <summary>
    /// Abstraction over the UI thread marshaling primitive so ViewModels and services
    /// can post work to the UI thread in a testable way.
    /// </summary>
    public interface IUiDispatcher
    {
        /// <summary>
        /// Executes <paramref name="action"/> on the UI thread. When the caller is already
        /// on the UI thread (or no UI thread is available, e.g. tests) the action runs
        /// synchronously; otherwise it is queued asynchronously.
        /// </summary>
        void Post(Action action);
    }
}
