// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using Avalonia.Controls;
using Avalonia.Interactivity;
using Material.Dialog;
using Material.Dialog.Interfaces;
using Material.Dialog.Views;
using AetherAprs.ViewModels;
using AetherAprs.Configuration;

namespace AetherAprs.Views;

public partial class PortsView : UserControl
{
    public PortsView()
    {
        InitializeComponent();
        AddPortButton.Click += OnAddPortClick;
    }

    private async void OnAddPortClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not PortsViewModel viewModel)
        {
            return;
        }

        TopLevel? topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is not Window ownerWindow)
        {
            return;
        }

        // Get global callsign
        var configService = App.GetService<AetherAprs.Services.IConfigurationService>();
        var globalCallsign = configService.Settings.Aprs.Callsign;
        var nextNumber = viewModel.GetNextPortNumber();

        // Create dialog content
        var dialogContent = new NewPortDialogContent();
        var dialogVm = new NewPortDialogViewModel(globalCallsign, nextNumber);
        dialogContent.DataContext = dialogVm;

        var dialog = DialogHelper.CreateCustomDialog(new CustomDialogBuilderParams
        {
            WindowTitle = "Add New Port",
            ContentHeader = "New Port",
            SupportingText = "Configure your new APRS port.",
            Content = dialogContent,
            Width = 420,
            DialogButtons = new DialogButton[]
            {
                new() { Content = "Cancel", Result = DialogHelper.DIALOG_RESULT_CANCEL, IsNegative = true },
                new() { Content = "Add Port", Result = DialogHelper.DIALOG_RESULT_OK, IsPositive = true }
            }
        });

        var result = await dialog.ShowDialog(ownerWindow);

        if (result.GetResult == DialogHelper.DIALOG_RESULT_OK)
        {
            var config = dialogVm.BuildConfig();
            viewModel.AddPortCommand.Execute(config);
        }
    }
}