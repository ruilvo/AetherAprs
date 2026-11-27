// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using AetherAprs.ViewModels.Pages;
using Avalonia.Controls;
using Avalonia.Input;

namespace AetherAprs.Views.Pages;

public partial class SupportPage : UserControl
{
    private SupportViewModel? _currentViewModel;

    public SupportPage()
    {
        InitializeComponent();
        KeyDown += OnKeyDown;
        DataContextChanged += OnDataContextChanged;
    }

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        BindViewModel(DataContext as SupportViewModel);
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        // The page is transient while the storage monitor is a singleton: release
        // the subscription here so a closed Support page is collectable.
        UnbindViewModel();
        base.OnDetachedFromVisualTree(e);
    }

    private void OnDataContextChanged(object? sender, EventArgs e) =>
        BindViewModel(DataContext as SupportViewModel);

    private void BindViewModel(SupportViewModel? viewModel)
    {
        if (ReferenceEquals(_currentViewModel, viewModel))
        {
            return;
        }

        UnbindViewModel();
        _currentViewModel = viewModel;
    }

    private void UnbindViewModel()
    {
        _currentViewModel?.Dispose();
        _currentViewModel = null;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && DataContext is SupportViewModel viewModel)
        {
            if (viewModel.CloseCommand.CanExecute(null))
            {
                viewModel.CloseCommand.Execute(null);
            }

            e.Handled = true;
        }
    }
}
