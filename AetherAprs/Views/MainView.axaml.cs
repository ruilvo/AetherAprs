// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later
using AetherAprs.Services.Navigation;
using AetherAprs.ViewModels;
using Avalonia.Controls;
using BruTile.Wms;

namespace AetherAprs.Views;

public partial class MainView : UserControl
{
    public MainView()
    {
        InitializeComponent();

        if (Design.IsDesignMode)
        {
            DataContext = null;
        }
    }
}
