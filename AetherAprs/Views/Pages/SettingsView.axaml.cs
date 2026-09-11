// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using DialogHostAvalonia;
using AetherAprs.Services;
using AetherAprs.ViewModels.Pages;
using AetherAprs.Views.Pages;

namespace AetherAprs.Views.Pages;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
    }

    private async void OnConfigureBeaconingClick(object? sender, RoutedEventArgs e)
    {
        // Get services
        var beaconService = App.GetService<IBeaconService>();

        // Create the beaconing settings view and view model
        var beaconingView = new DynamicBeaconingView();
        var beaconingVm = new DynamicBeaconingViewModel(beaconService);
        beaconingView.DataContext = beaconingVm;

        // Show as dialog
        var result = await DialogHost.Show(beaconingView, "MainDialogHost");
    }
}


