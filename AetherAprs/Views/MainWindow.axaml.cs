// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later
using Avalonia.Controls;
using AetherAprs.ViewModels;

namespace AetherAprs.Views;

public partial class MainWindow : Window
{
    public MainWindow() : this(null)
    {
    }

    public MainWindow(MainViewModel? viewModel)
    {
        InitializeComponent();
        if (viewModel is not null)
        {
            DataContext = viewModel;
        }
    }
}
