// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Platform;
using Microsoft.Extensions.Logging;
using System;

namespace AetherAprs.Services.UI;

/// <summary>
/// Service that tracks on-screen keyboard visibility and provides padding adjustments.
/// </summary>
public class KeyboardInsetsService(ILogger<KeyboardInsetsService>? logger = null) : IKeyboardInsetsService
{
    private Thickness _currentInsets = new(0);
    private IInputPane? _inputPane;

    public Thickness CurrentInsets
    {
        get => _currentInsets;
        private set
        {
            if (_currentInsets != value)
            {
                _currentInsets = value;
                InsetsChanged?.Invoke(this, value);
            }
        }
    }

    public event EventHandler<Thickness>? InsetsChanged;

    public void Initialize(TopLevel topLevel)
    {
        ArgumentNullException.ThrowIfNull(topLevel);

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

    private void OnInputPaneStateChanged(object? sender, InputPaneStateEventArgs e)
    {
        try
        {
            // When keyboard is visible, add bottom padding equal to keyboard height
            // When hidden, remove the padding
            var keyboardHeight = e.NewState == InputPaneState.Open ? e.EndRect.Height : 0;
            
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
}
