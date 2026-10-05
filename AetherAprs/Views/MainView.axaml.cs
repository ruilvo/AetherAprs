// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later
using AetherAprs.Services.UI;
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
            // Get the keyboard insets service and initialize it with the TopLevel
            var app = Avalonia.Application.Current as App;
            var keyboardService = app?.ServiceProvider.GetService(typeof(IKeyboardInsetsService)) as IKeyboardInsetsService;
            var topLevel = TopLevel.GetTopLevel(this);

            if (keyboardService != null && topLevel != null)
            {
                keyboardService.Initialize(topLevel);
            }
        }
        catch
        {
            // Ignore errors - keyboard insets are optional
        }
    }
}
