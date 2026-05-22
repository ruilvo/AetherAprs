// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Models.Geo;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AetherAprs.ViewModels;

/// <summary>
/// View model for the home page, displaying the APRS map view.
/// </summary>
public partial class HomeViewModel : ViewModelBase
{
    [ObservableProperty]
    private GeoLocation? _markerLocation;

    public HomeViewModel()
    {
        // Example location for demonstration (e.g. Lisbon)
        MarkerLocation = new GeoLocation(38.7223, -9.1393);
    }
}
