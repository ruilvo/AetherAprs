// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.ViewModels.Pages;

using Avalonia.Controls;

namespace AetherAprs.Views.Pages;

public partial class PacketDetailsPage : UserControl
{
    private const double LoadMoreThreshold = 64;

    public PacketDetailsPage() => InitializeComponent();

    private void OnListScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (DataContext is not PacketDetailsViewModel viewModel || !viewModel.HasMore)
        {
            return;
        }

        // Every tab owns a ListBox; the attached event reports its scroll viewer as
        // the source, so a tab that never scrolled cannot trigger a load.
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
