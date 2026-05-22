// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Messages;
using AetherAprs.Models.Geo;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using System.Collections.Generic;

namespace AetherAprs.ViewModels;

/// <summary>
/// View model for the home page, displaying the APRS map view.
/// </summary>
public partial class HomeViewModel : ViewModelBase
{
    [ObservableProperty]
    public partial List<GeoLocation> MarkerLocations { get; set; } 

    public HomeViewModel()
    {
        MarkerLocations =
        [
            // Example location for demonstration (e.g. Lisbon)
            new GeoLocation(38.7223, -9.1393),
        ];

        WeakReferenceMessenger.Default.Register<HomeViewModel, MapMarkersRequestMessage>(this, (r, m) =>
        {
            m.Reply(r.MarkerLocations);
        });
    }

    partial void OnMarkerLocationsChanged(List<GeoLocation> value)
    {
        WeakReferenceMessenger.Default.Send(new MapMarkersChangedMessage(value));
    }
}
