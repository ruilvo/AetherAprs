// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using Avalonia.Controls;
using Avalonia.Input;
using AetherAprs.ViewModels.Pages;

namespace AetherAprs.Views.Pages;

public partial class DynamicBeaconingView : UserControl
{
    public DynamicBeaconingView()
    {
        InitializeComponent();
        KeyDown += OnKeyDown;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && DataContext is DynamicBeaconingViewModel viewModel)
        {
            if (viewModel.CloseCommand.CanExecute(null))
            {
                viewModel.CloseCommand.Execute(null);
            }

            e.Handled = true;
        }
    }
}
