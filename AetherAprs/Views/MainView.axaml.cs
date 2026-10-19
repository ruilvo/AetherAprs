// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later
using AetherAprs.ViewModels;
using Avalonia.Controls;
using System;

namespace AetherAprs.Views;

public partial class MainView : UserControl
{
    public MainView()
    {
        InitializeComponent();

        // Initialize keyboard insets service when view is loaded
        Loaded += OnLoaded;
    }

    private void OnLoaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Loaded -= OnLoaded;

        try
        {
            // Keyboard insets are wired through the ViewModel to avoid
            // service-locator use in the view.
            var topLevel = TopLevel.GetTopLevel(this);
            if (DataContext is MainViewModel viewModel && topLevel != null)
            {
                viewModel.InitializeKeyboardInsets(topLevel);
            }
        }
        catch
        {
            // Ignore errors - keyboard insets are optional
        }
    }
}
