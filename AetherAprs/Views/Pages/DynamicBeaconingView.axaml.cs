// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using Avalonia.Controls;
using Avalonia.Input;
using DialogHostAvalonia;

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
        if (e.Key == Key.Escape)
        {
            DialogHost.Close("MainDialogHost");
            e.Handled = true;
        }
    }
}
