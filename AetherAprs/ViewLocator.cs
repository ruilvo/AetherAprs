// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.ViewModels;
using AetherAprs.ViewModels.Components;
using AetherAprs.ViewModels.Pages;
using AetherAprs.Views;
using AetherAprs.Views.Components;
using AetherAprs.Views.Pages;
using Avalonia.Controls;
using Avalonia.Controls.Templates;

namespace AetherAprs;

/// <summary>
/// Given a view model, returns the corresponding view if possible.
/// </summary>
public class ViewLocator : IDataTemplate
{
    public Control? Build(object? param)
    {
        return param switch
        {
            MainViewModel => new MainView(),
            HomeViewModel => new HomePage(),
            MessagesViewModel => new MessagesPage(),
            PacketsViewModel => new PacketsPage(),
            PortsViewModel => new PortsPage(),
            SettingsViewModel => new SettingsPage(),
            DynamicBeaconingViewModel => new DynamicBeaconingPage(),
            AddEditPortViewModel => new AddEditPortPage(),
            ConversationViewModel => new ConversationPage(),
            PacketDetailsViewModel => new PacketDetailsPage(),
            PortItemViewModel => new PortItemComponent(),
            BeaconTransmissionViewModel => new BeaconTransmissionComponent(),
            LocationTrackingViewModel => new LocationTrackingComponent(),
            SymbolSelectorViewModel => new SymbolSelectorComponent(),
            _ => param is null
                ? null
                : new TextBlock { Text = $"No view for {param.GetType().Name}" }
        };
    }

    public bool Match(object? data)
    {
        return data is ViewModelBase;
    }
}
