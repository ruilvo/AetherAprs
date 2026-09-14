// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using AetherAprs.ViewModels;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace AetherAprs.Views.Components;

public partial class PortItemView : UserControl
{
    public PortItemView()
    {
        InitializeComponent();
    }

    private void OnPortClicked(object? sender, RoutedEventArgs e)
    {
        // Bubble the event up to the parent PortsView
        var args = new RoutedEventArgs(PortClickedEvent, this);
        RaiseEvent(args);
    }

    private void OnDeleteClicked(object? sender, RoutedEventArgs e)
    {
        // Bubble the event up to the parent PortsView
        var args = new RoutedEventArgs(DeleteClickedEvent, this);
        RaiseEvent(args);
    }

    private void OnShowOnMapClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is PortItemViewModel item)
        {
            item.ShowOnMap = !item.ShowOnMap;
        }
    }

    // Define routed events for parent to handle
    public static readonly RoutedEvent<RoutedEventArgs> PortClickedEvent =
        RoutedEvent.Register<PortItemView, RoutedEventArgs>(nameof(PortClicked), RoutingStrategies.Bubble);

    public static readonly RoutedEvent<RoutedEventArgs> DeleteClickedEvent =
        RoutedEvent.Register<PortItemView, RoutedEventArgs>(nameof(DeleteClicked), RoutingStrategies.Bubble);

    public event EventHandler<RoutedEventArgs> PortClicked
    {
        add => AddHandler(PortClickedEvent, value);
        remove => RemoveHandler(PortClickedEvent, value);
    }

    public event EventHandler<RoutedEventArgs> DeleteClicked
    {
        add => AddHandler(DeleteClickedEvent, value);
        remove => RemoveHandler(DeleteClickedEvent, value);
    }
}
