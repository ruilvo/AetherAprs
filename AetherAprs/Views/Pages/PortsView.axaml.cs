// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using Avalonia.Controls;
using Avalonia.Interactivity;
using DialogHostAvalonia;
using AetherAprs.ViewModels;
using AetherAprs.Views.Dialogues;

namespace AetherAprs.Views.Pages;

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

        // Get global callsign
        var configService = App.GetService<AetherAprs.Services.IConfigurationService>();
        var globalCallsign = configService.Settings.Aprs.Callsign;
        var nextNumber = viewModel.GetNextPortNumber();

        // Create dialog content
        var dialogContent = new AddEditPortDialogView();
        var dialogVm = new AddEditPortDialogViewModel(globalCallsign, nextNumber);
        dialogContent.DataContext = dialogVm;

        var result = await DialogHost.Show(dialogContent, "MainDialogHost");

        if (result is "OK")
        {
            var config = dialogVm.BuildConfig();
            viewModel.AddPortCommand.Execute(config);
        }
    }
}
