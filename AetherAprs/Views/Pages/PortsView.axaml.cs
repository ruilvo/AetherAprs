// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using DialogHostAvalonia;
using AetherAprs.ViewModels;
using AetherAprs.Views.Dialogs;
using AetherAprs.Imaging;

namespace AetherAprs.Views.Pages;

public partial class PortsView : UserControl
{
    private PortsViewModel? _viewModel;

    public PortsView()
    {
        InitializeComponent();
        AddPortButton.Click += OnAddPortClick;
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        _viewModel = DataContext as PortsViewModel;
        if (_viewModel is not null)
        {
            _viewModel.EditPortCommand.CanExecuteChanged += EditPortCommand_CanExecuteChanged;
        }
    }

    private void EditPortCommand_CanExecuteChanged(object? sender, EventArgs e)
    {
        // This is called when the command state changes
    }

    private void OnPortClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.DataContext is PortItemViewModel item)
        {
            OnEditPort(item);
        }
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
        var dialogVm = new AddEditPortDialogViewModel(
            globalCallsign,
            nextNumber,
            App.GetService<IAprsSymbolBitmapProvider>());
        dialogContent.DataContext = dialogVm;

        var result = await DialogHost.Show(dialogContent, "MainDialogHost");

        if (result is "OK")
        {
            var config = dialogVm.BuildConfig();
            viewModel.AddPortCommand.Execute(config);
        }
    }

    public async void OnEditPort(PortItemViewModel item)
    {
        if (DataContext is not PortsViewModel viewModel)
        {
            return;
        }

        // Get global callsign
        var configService = App.GetService<AetherAprs.Services.IConfigurationService>();
        var globalCallsign = configService.Settings.Aprs.Callsign;

        // Create dialog content for editing
        var dialogContent = new AddEditPortDialogView();
        var dialogVm = new AddEditPortDialogViewModel(
            globalCallsign,
            viewModel.GetNextPortNumber(),
            App.GetService<IAprsSymbolBitmapProvider>());

        // Populate with existing values
        dialogVm.Name = item.Name;
        dialogVm.IsRx = item.IsRx;
        dialogVm.IsTx = item.IsTx;
        dialogVm.SymbolTableCharacter = item.SymbolTableCharacter;
        dialogVm.SymbolCodeCharacter = item.SymbolCodeCharacter;

        dialogContent.DataContext = dialogVm;

        var result = await DialogHost.Show(dialogContent, "MainDialogHost");

        if (result is "OK")
        {
            var updatedConfig = dialogVm.BuildConfig();
            updatedConfig.Id = item.Id;
            updatedConfig.Type = item.Type;
            updatedConfig.IsEnabled = item.IsEnabled;
            await viewModel.UpdatePortAsync(updatedConfig);
        }
    }
}
