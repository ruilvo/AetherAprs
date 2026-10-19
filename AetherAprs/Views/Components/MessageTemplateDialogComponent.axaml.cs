// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.ViewModels.Components;
using Avalonia.Controls;
using Avalonia.Interactivity;
using DialogHostAvalonia;
using System;

namespace AetherAprs.Views.Components;

public partial class MessageTemplateDialogComponent : UserControl
{
    private MessageTemplateDialogViewModel? _currentViewModel;

    public MessageTemplateDialogComponent()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        // Unsubscribe from old ViewModel to prevent memory leak
        if (_currentViewModel != null)
        {
            _currentViewModel.CloseRequested -= OnCloseRequested;
        }

        _currentViewModel = DataContext as MessageTemplateDialogViewModel;

        if (_currentViewModel != null)
        {
            _currentViewModel.CloseRequested += OnCloseRequested;
        }
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        // Unsubscribe when the dialog is torn down to prevent memory leak
        if (_currentViewModel != null)
        {
            _currentViewModel.CloseRequested -= OnCloseRequested;
            _currentViewModel = null;
        }

        base.OnDetachedFromVisualTree(e);
    }

    private void OnCloseRequested(object? sender, EventArgs e)
    {
        DialogHost.Close("MainDialogHost", _currentViewModel?.Result);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        DialogHost.Close("MainDialogHost", null);
    }
}
