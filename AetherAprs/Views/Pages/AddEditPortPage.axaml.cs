// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using AetherAprs.ViewModels.Pages;
using Avalonia.Controls;

namespace AetherAprs.Views.Pages;

public partial class AddEditPortPage : UserControl
{
    private AddEditPortViewModel? _currentViewModel;

    public AddEditPortPage()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        // Dispose previous ViewModel to cancel BLE scans and cleanup resources
        if (_currentViewModel != null)
        {
            _currentViewModel.Dispose();
            _currentViewModel = null;
        }

        // Track new ViewModel
        _currentViewModel = DataContext as AddEditPortViewModel;
    }
}