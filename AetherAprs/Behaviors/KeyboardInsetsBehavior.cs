// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Services.UI;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Xaml.Interactivity;
using System;

namespace AetherAprs.Behaviors;

/// <summary>
/// Behavior that automatically adjusts margin when the on-screen keyboard appears.
/// Attach this to any Control to add bottom margin when keyboard is visible.
/// </summary>
public class KeyboardInsetsBehavior : Behavior<Control>
{
    private IKeyboardInsetsService? _keyboardService;
    private Thickness _originalMargin;

    protected override void OnAttached()
    {
        base.OnAttached();

        if (AssociatedObject == null)
        {
            return;
        }

        // Get the keyboard insets service
        var app = Application.Current as App;
        _keyboardService = app?.ServiceProvider.GetService(typeof(IKeyboardInsetsService)) as IKeyboardInsetsService;

        if (_keyboardService == null)
        {
            return;
        }

        // Store original margin
        _originalMargin = AssociatedObject.Margin;

        // Subscribe to keyboard insets changes
        _keyboardService.InsetsChanged += OnKeyboardInsetsChanged;

        // Apply current insets immediately
        ApplyInsets(_keyboardService.CurrentInsets);
    }

    protected override void OnDetaching()
    {
        if (_keyboardService != null)
        {
            _keyboardService.InsetsChanged -= OnKeyboardInsetsChanged;
        }

        // Restore original margin
        if (AssociatedObject != null)
        {
            AssociatedObject.Margin = _originalMargin;
        }

        base.OnDetaching();
    }

    private void OnKeyboardInsetsChanged(object? sender, Thickness insets)
    {
        ApplyInsets(insets);
    }

    private void ApplyInsets(Thickness insets)
    {
        if (AssociatedObject == null)
        {
            return;
        }

        // Add keyboard insets to the original margin
        AssociatedObject.Margin = new Thickness(
            _originalMargin.Left,
            _originalMargin.Top,
            _originalMargin.Right,
            _originalMargin.Bottom + insets.Bottom);
    }
}
