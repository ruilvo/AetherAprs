// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using Avalonia;
using Avalonia.Threading;

namespace AetherAprs.Services.UI
{
    /// <summary>
    /// Avalonia-backed <see cref="IUiDispatcher"/> that marshals work onto
    /// <see cref="Dispatcher.UIThread"/>, following the same CheckAccess/Post
    /// pattern used by <c>MessageService.RunOnUi</c>.
    /// </summary>
    public sealed class AvaloniaUiDispatcher : IUiDispatcher
    {
        public static AvaloniaUiDispatcher Instance { get; } = new();

        public void Post(Action action)
        {
            ArgumentNullException.ThrowIfNull(action);

            if (Application.Current is null || Dispatcher.UIThread.CheckAccess())
            {
                action();
                return;
            }

            Dispatcher.UIThread.Post(action);
        }
    }
}
