// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.ViewModels.Pages;
using Avalonia.Controls;
using Avalonia.Input;
using System;

namespace AetherAprs.Views.Pages;

public partial class ObjectsPage : UserControl
{
    private ObjectsViewModel? _currentViewModel;

    public ObjectsPage()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        KeyDown += OnKeyDown;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && DataContext is ObjectsViewModel viewModel)
        {
            if (viewModel.CloseCommand.CanExecute(null))
            {
                viewModel.CloseCommand.Execute(null);
            }

            e.Handled = true;
        }
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        _currentViewModel = DataContext as ObjectsViewModel;
        if (_currentViewModel != null)
        {
            _ = RefreshAsync();
        }
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        _currentViewModel = null;
        base.OnDetachedFromVisualTree(e);
    }

    private async System.Threading.Tasks.Task RefreshAsync()
    {
        try
        {
            if (_currentViewModel != null)
            {
                await _currentViewModel.RefreshAsync();
            }
        }
        catch (Exception)
        {
            // Swallow refresh failures: there is no logger in code-behind and an
            // unhandled exception in an async void handler would crash the app.
        }
    }
}
