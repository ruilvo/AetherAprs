// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using DialogHostAvalonia;
using AetherAprs.ViewModels;
using AetherAprs.Views.Components;
using AetherAprs.Imaging;
using AetherAprs.Services.Bluetooth;
using AetherAprs.Transports.Kiss;

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
        if (e.Source is PortItemView portItemView && portItemView.DataContext is PortItemViewModel item)
        {
            OnEditPort(item);
        }
    }

    private void OnDeleteClicked(object? sender, RoutedEventArgs e)
    {
        if (e.Source is PortItemView portItemView &&
            portItemView.DataContext is PortItemViewModel item &&
            DataContext is PortsViewModel viewModel)
        {
            viewModel.DeletePortCommand.Execute(item);
        }
    }

    private async void OnAddPortClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not PortsViewModel viewModel)
        {
            return;
        }

        var configService = App.GetService<AetherAprs.Services.IConfigurationService>();
        var globalCallsign = configService.Settings.Aprs.Callsign;
        var nextNumber = viewModel.GetNextPortNumber();

        var dialogContent = new AddEditPortView();
        var dialogVm = CreateDialogViewModel(
            globalCallsign,
            nextNumber,
            configService);
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

        var configService = App.GetService<AetherAprs.Services.IConfigurationService>();
        var globalCallsign = configService.Settings.Aprs.Callsign;

        var dialogContent = new AddEditPortView();
        var dialogVm = CreateDialogViewModel(
            globalCallsign,
            viewModel.GetNextPortNumber(),
            configService);

        dialogVm.PopulateFrom(item);

        dialogContent.DataContext = dialogVm;

        var result = await DialogHost.Show(dialogContent, "MainDialogHost");

        if (result is "OK")
        {
            var updatedConfig = dialogVm.BuildConfig();
            updatedConfig.Id = item.Id;
            updatedConfig.IsEnabled = item.IsEnabled;
            await viewModel.UpdatePortAsync(updatedConfig);
        }
    }

    private static AddEditPortDialogViewModel CreateDialogViewModel(
        string globalCallsign,
        int nextPortNumber,
        AetherAprs.Services.IConfigurationService configService)
    {
        return new AddEditPortDialogViewModel(
            globalCallsign,
            nextPortNumber,
            App.GetService<IAprsSymbolBitmapProvider>(),
            App.GetService<IKissStreamFactory>(),
            App.GetService<IBluetoothLeScanner>(),
            App.GetService<IBluetoothClassicDeviceProvider>(),
            defaultSymbolTableCharacter: configService.Settings.Aprs.DefaultSymbolTableCharacter,
            defaultSymbolCodeCharacter: configService.Settings.Aprs.DefaultSymbolCodeCharacter,
            defaultBeaconMode: configService.Settings.Aprs.DefaultBeaconMode);
    }
}