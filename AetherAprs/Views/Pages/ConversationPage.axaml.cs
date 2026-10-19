// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Models.Messaging;
using AetherAprs.ViewModels.Pages;
using AetherAprs.Views.Components;
using Avalonia.Controls;
using Avalonia.Interactivity;
using DialogHostAvalonia;
using System;

namespace AetherAprs.Views.Pages;

public partial class ConversationPage : UserControl
{
    public ConversationPage()
    {
        InitializeComponent();
    }

    private async void OnTemplateButtonClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (DataContext is not ConversationViewModel viewModel)
            {
                return;
            }

            var dialogViewModel = viewModel.CreateTemplateDialog();
            var result = await DialogHost.Show(
                new MessageTemplateDialogComponent { DataContext = dialogViewModel }, "MainDialogHost");

            if (result is MessageTemplateDialogResult templateResult)
            {
                viewModel.ApplyTemplateResult(templateResult);
            }
        }
        catch (Exception)
        {
            // Swallow dialog failures: there is no logger in code-behind and an
            // unhandled exception in an async void handler would crash the app.
        }
    }
}
