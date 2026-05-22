// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Models.Geo;
using Avalonia;
using Avalonia.Controls;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Projections;
using Mapsui.Providers;
using Mapsui.Providers.Wms;
using Mapsui.Styles;
using Mapsui.Tiling;
using System.Collections.Generic;
using System.Linq;

namespace AetherAprs.Views;

public partial class HomeView : UserControl
{
    // To interact with the ViewModel: custom direct property for MarkerLocations
    public static readonly DirectProperty<HomeView, List<GeoLocation>?> MarkerLocationsProperty =
        AvaloniaProperty.RegisterDirect<HomeView, List<GeoLocation>?>(
            nameof(MarkerLocations),
            o => o.MarkerLocations,
            (o, v) => o.MarkerLocations = v);
    private List<GeoLocation>? _markerLocations;
    public List<GeoLocation>? MarkerLocations
    {
        get => _markerLocations;
        set => SetAndRaise(MarkerLocationsProperty, ref _markerLocations, value);
    }

    // For MapsUi
    private MemoryLayer _markerLayer;

    private List<IFeature> CreateFeaturesFromMarkerLocations()
    {
        return MarkerLocations?
            .Select(x => (IFeature)new PointFeature(SphericalMercator.FromLonLat(x.Longitude, x.Latitude)))
            .ToList()
            ?? [];
    }

    public HomeView()
    {
        InitializeComponent();

        MapControl.Map.Layers.Add(OpenStreetMap.CreateTileLayer());

        _markerLayer = new MemoryLayer
        {
            Name = "MarkerLayer",
            Style = new SymbolStyle
            {
                SymbolScale = 0.5,
                Fill = new Brush(Color.Red)
            },
            Features = []
        };
        MapControl.Map.Layers.Add(_markerLayer);
    }
}
