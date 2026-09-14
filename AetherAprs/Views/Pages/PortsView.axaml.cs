// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using AetherAprs.ViewModels;
using AetherAprs.Views.Components;

namespace AetherAprs.Views.Pages;

public partial class PortsView : UserControl
{
    private PortsViewModel? _viewModel;

    public PortsView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        _viewModel = DataContext as PortsViewModel;
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

    private void OnAddPortClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not PortsViewModel viewModel)
        {
            return;
        }

        viewModel.AddPortCommand.Execute(null);
    }

    private void OnEditPort(PortItemViewModel item)
    {
        if (DataContext is not PortsViewModel viewModel)
        {
            return;
        }

        viewModel.OnEditPort(item);
    }
}
