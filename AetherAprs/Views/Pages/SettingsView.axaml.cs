// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using DialogHostAvalonia;
using Material.Styles.Controls;
using Material.Styles.Models;
using AetherAprs.Services;
using AetherAprs.ViewModels;
using AetherAprs.ViewModels.Pages;
using AetherAprs.Views.Pages;

namespace AetherAprs.Views.Pages;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is SettingsViewModel viewModel)
        {
            viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsViewModel.IsSaved))
        {
            if (sender is SettingsViewModel viewModel && viewModel.IsSaved)
            {
                SnackbarHost.Post(
                    new SnackbarModel(
                        "✓ Settings saved!",
                        TimeSpan.FromSeconds(3)),
                    SettingsSnackbarHost.HostName,
                    DispatcherPriority.Normal);

                viewModel.IsSaved = false;
            }
        }
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


