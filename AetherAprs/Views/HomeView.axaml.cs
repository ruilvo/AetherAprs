// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Collections.Generic;
using AetherAprs.Models.Geo;
using Avalonia;
using Avalonia.Controls;
using Mapsui;
using Mapsui.Projections;
using Mapsui.Layers;
using Mapsui.Providers;
using Mapsui.Styles;
using Mapsui.Tiling;

namespace AetherAprs.Views;

public partial class HomeView : UserControl
{
    public static readonly StyledProperty<GeoLocation?> MarkerLocationProperty =
        AvaloniaProperty.Register<HomeView, GeoLocation?>(nameof(MarkerLocation));

    public GeoLocation? MarkerLocation
    {
        get => GetValue(MarkerLocationProperty);
        set => SetValue(MarkerLocationProperty, value);
    }

    private MemoryLayer _markerLayer;
    private PointFeature _markerFeature;

    public HomeView()
    {
        InitializeComponent();

        MapControl.Map.Layers.Add(OpenStreetMap.CreateTileLayer());

        _markerFeature = new PointFeature(new MPoint(0, 0));
        _markerLayer = new MemoryLayer
        {
            Name = "MarkerLayer",
            Style = new SymbolStyle
            {
                SymbolScale = 0.5,
                Fill = new Brush(Color.Red)
            },
            Features = new List<IFeature> { _markerFeature }
        };
        MapControl.Map.Layers.Add(_markerLayer);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == MarkerLocationProperty)
        {
            if (change.NewValue is GeoLocation location)
            {
                var (x, y) = SphericalMercator.FromLonLat(location.Longitude, location.Latitude);
                var mpoint = new MPoint(x, y);

                _markerFeature = new PointFeature(mpoint);
                _markerLayer.Features = new List<IFeature> { _markerFeature };
                _markerLayer.DataHasChanged();

                // Navigate to it
                MapControl.Map.Navigator.CenterOn(mpoint);
                MapControl.RefreshData();
            }
        }
    }
}
