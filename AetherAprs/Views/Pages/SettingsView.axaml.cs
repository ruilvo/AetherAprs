// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Threading;
using Material.Styles.Controls;
using Material.Styles.Models;
using AetherAprs.ViewModels;

namespace AetherAprs.Views.Pages;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is SettingsViewModel viewModel)
        {
            viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsViewModel.IsSaved))
        {
            if (sender is SettingsViewModel viewModel && viewModel.IsSaved)
            {
                SnackbarHost.Post(
                    new SnackbarModel(
                        "✓ Settings saved!",
                        TimeSpan.FromSeconds(3)),
                    "MainSnackbarHost",
                    DispatcherPriority.Normal);

                viewModel.IsSaved = false;
            }
        }
    }
}
