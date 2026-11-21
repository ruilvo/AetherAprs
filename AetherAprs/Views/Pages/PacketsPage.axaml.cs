// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using Avalonia.Controls;
using AetherAprs.ViewModels.Pages;

namespace AetherAprs.Views.Pages;

public partial class PacketsPage : UserControl
{
    private const double LoadMoreThreshold = 64;

    public PacketsPage()
    {
        InitializeComponent();
        PacketsScroll.ScrollChanged += OnScrollChanged;
    }

    private void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (DataContext is not PacketsViewModel viewModel || !viewModel.HasMore)
        {
            return;
        }

        var scrollViewer = PacketsScroll;
        var distanceToBottom = scrollViewer.Extent.Height - scrollViewer.Viewport.Height - scrollViewer.Offset.Y;
        if (distanceToBottom <= LoadMoreThreshold)
        {
            viewModel.LoadMoreCommand.Execute(null);
        }
    }
}
