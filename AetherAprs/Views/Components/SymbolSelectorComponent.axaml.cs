// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.ViewModels.Components;
using Avalonia.Controls;
using Avalonia.Interactivity;
using System;

namespace AetherAprs.Views.Components;

public partial class SymbolSelectorComponent : UserControl
{
    public event EventHandler? CancelRequested;

    public SymbolSelectorComponent()
    {
        InitializeComponent();
        AttachedToVisualTree += OnAttachedToVisualTree;
    }

    private void OnAttachedToVisualTree(object? sender, EventArgs e)
    {
        if (DataContext is SymbolSelectorViewModel viewModel)
        {
            _ = viewModel.LoadPreviewsAsync();
        }
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => CancelRequested?.Invoke(this, EventArgs.Empty);
}
