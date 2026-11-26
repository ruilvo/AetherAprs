// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Localization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using DialogHostAvalonia;
using System;
using System.Threading.Tasks;

namespace AetherAprs.Services.UI;

/// <summary>
/// Shows confirmation dialogs through the application's main DialogHost overlay.
/// </summary>
public sealed class DialogHostConfirmationService : IConfirmationDialogService
{
    public const string MainDialogHostIdentifier = "MainDialogHost";

    public async Task<bool> ShowConfirmationAsync(string title, string message)
    {
        Control content = BuildContent(title, message);
        object? result = await DialogHost.Show(content, MainDialogHostIdentifier);
        return result is true;
    }

    private static Control BuildContent(string title, string message)
    {
        TextBlock titleBlock = new()
        {
            Text = title,
            Margin = new Thickness(24, 24, 24, 8),
            TextWrapping = TextWrapping.Wrap
        };
        titleBlock.Classes.Add("Headline6");

        TextBlock messageBlock = new()
        {
            Text = message,
            Margin = new Thickness(24, 0, 24, 16),
            TextWrapping = TextWrapping.Wrap
        };
        messageBlock.Classes.Add("Body2");

        Button cancelButton = new()
        {
            Content = Strings.Get("Cancel"),
            MinWidth = 88,
            MinHeight = 48,
            Padding = new Thickness(16, 0),
            HorizontalAlignment = HorizontalAlignment.Right
        };
        cancelButton.Click += (_, _) => DialogHost.Close(MainDialogHostIdentifier, false);

        Button confirmButton = new()
        {
            Content = Strings.Get("Confirm"),
            MinWidth = 88,
            MinHeight = 48,
            Padding = new Thickness(16, 0),
            HorizontalAlignment = HorizontalAlignment.Right
        };
        confirmButton.Click += (_, _) => DialogHost.Close(MainDialogHostIdentifier, true);

        StackPanel buttonPanel = new()
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(24, 0, 24, 24)
        };
        buttonPanel.Children.Add(cancelButton);
        buttonPanel.Children.Add(confirmButton);

        StackPanel panel = new()
        {
            MaxWidth = 400
        };
        panel.Children.Add(titleBlock);
        panel.Children.Add(messageBlock);
        panel.Children.Add(buttonPanel);

        return panel;
    }
}
