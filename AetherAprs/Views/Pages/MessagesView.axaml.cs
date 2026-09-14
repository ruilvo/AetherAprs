// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using Avalonia.Controls;
using Avalonia.Interactivity;
using AetherAprs.Models.Messaging;
using AetherAprs.ViewModels;

namespace AetherAprs.Views.Pages;

public partial class MessagesView : UserControl
{
    public MessagesView()
    {
        InitializeComponent();
        NewMessageButton.Click += OnNewMessageClick;
    }

    private void OnNewMessageClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MessagesViewModel viewModel)
        {
            viewModel.NewMessageCommand.Execute(null);
        }
    }

    private void OnConversationClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MessagesViewModel viewModel)
        {
            return;
        }

        if (sender is Button { Tag: ConversationThread thread })
        {
            viewModel.OpenConversationCommand.Execute(thread);
        }
    }
}
