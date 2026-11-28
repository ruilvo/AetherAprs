// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using Avalonia.Threading;

using Material.Styles.Controls;

namespace AetherAprs.Services.UI;

public sealed class SnackbarTransientFeedbackService : ITransientFeedbackService
{
    public void Show(string message) => Dispatcher.UIThread.Post(
            () => SnackbarHost.Post(message, "MainSnackbarHost", DispatcherPriority.Normal));
}
