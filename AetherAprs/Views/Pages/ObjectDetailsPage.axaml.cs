// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using Avalonia.Controls;
using AetherAprs.ViewModels.Pages;

namespace AetherAprs.Views.Pages;

public partial class ObjectDetailsPage : UserControl
{
    private const double LoadMoreThreshold = 64;

    public ObjectDetailsPage() => InitializeComponent();

    private void OnReportsScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (DataContext is not ObjectDetailsViewModel viewModel || !viewModel.HasMore)
        {
            return;
        }

        // The ListBox owns its scroll viewer; the attached event reports it as the source.
        if (e.Source is not ScrollViewer scrollViewer)
        {
            return;
        }

        double distanceToBottom = scrollViewer.Extent.Height - scrollViewer.Viewport.Height - scrollViewer.Offset.Y;
        if (distanceToBottom <= LoadMoreThreshold)
        {
            viewModel.LoadMoreCommand.Execute(null);
        }
    }
}
