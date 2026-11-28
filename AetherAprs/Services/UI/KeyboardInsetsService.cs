// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Platform;

using Microsoft.Extensions.Logging;

namespace AetherAprs.Services.UI;

/// <summary>
/// Service that tracks on-screen keyboard visibility and provides padding adjustments.
/// </summary>
public sealed class KeyboardInsetsService(ILogger<KeyboardInsetsService>? logger = null) : IKeyboardInsetsService
{
    private IInputPane? _inputPane;
    private bool _disposed;

    public Thickness CurrentInsets
    {
        get;
        private set
        {
            if (field != value)
            {
                field = value;
                InsetsChanged?.Invoke(this, value);
            }
        }
    } = new(0);

    public event EventHandler<Thickness>? InsetsChanged;

    public void Initialize(TopLevel topLevel)
    {
        ArgumentNullException.ThrowIfNull(topLevel);

        if (_disposed)
        {
            return;
        }

        // Unsubscribe from any previous pane so repeated Initialize calls do not
        // duplicate subscriptions (M7).
        UnsubscribeFromPane();

        // Get the InputPane (software keyboard) for this TopLevel
        _inputPane = topLevel.InputPane;

        if (_inputPane == null)
        {
            logger?.LogInformation("InputPane not supported on this platform");
            return;
        }

        // Subscribe to keyboard state changes
        _inputPane.StateChanged += OnInputPaneStateChanged;

        logger?.LogInformation("Keyboard insets service initialized");
    }

    private void UnsubscribeFromPane()
    {
        _inputPane?.StateChanged -= OnInputPaneStateChanged;
        _inputPane = null;
    }

    private void OnInputPaneStateChanged(object? sender, InputPaneStateEventArgs e)
    {
        try
        {
            // When keyboard is visible, add bottom padding equal to keyboard height
            // When hidden, remove the padding
            double keyboardHeight = e.NewState == InputPaneState.Open ? e.EndRect.Height : 0;

            CurrentInsets = new Thickness(0, 0, 0, keyboardHeight);

            logger?.LogDebug(
                "Keyboard state changed: {State}, Height: {Height}",
                e.NewState,
                keyboardHeight);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Error handling keyboard state change");
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        UnsubscribeFromPane();
    }
}
