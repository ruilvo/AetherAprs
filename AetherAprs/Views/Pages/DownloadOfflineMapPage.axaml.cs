// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.ViewModels.Pages;
using Avalonia.Controls;
using Avalonia.Input;
using Mapsui;
using Mapsui.Animations;
using Mapsui.Layers;
using Mapsui.Nts;
using Mapsui.Projections;
using Mapsui.Styles;
using Mapsui.Tiling;
using Mapsui.UI.Avalonia;
using NetTopologySuite.Geometries;
using System;
using System.ComponentModel;

namespace AetherAprs.Views.Pages;

public partial class DownloadOfflineMapPage : UserControl, IDisposable
{
    // OSM requires an identifiable User-Agent; generic defaults get blocked.
    private const string OsmUserAgent =
        "AetherAprs/1.0 (+https://github.com/ruilvo/AetherAprs)";

    private readonly WritableLayer _selectionLayer;
    private DownloadOfflineMapViewModel? _currentViewModel;
    private MPoint? _pendingCenter;
    private bool _drawing;
    private MPoint? _drawStartWorld;
    private bool _disposed;

    public DownloadOfflineMapPage()
    {
        InitializeComponent();
        _selectionLayer = new WritableLayer { Name = "Download selection", Style = null };
        InitializeMap();
        KeyDown += OnKeyDown;
        DataContextChanged += OnDataContextChanged;
        Loaded += (_, _) => TryApplyPendingCenter();
        SizeChanged += (_, _) => TryApplyPendingCenter();
    }

    private void InitializeMap()
    {
        SelectionMap.Map = new Map();
        SelectionMap.Map.Layers.Add(OpenStreetMap.CreateTileLayer(OsmUserAgent), group: -1);
        SelectionMap.Map.Layers.Add(_selectionLayer, group: 10);
        SelectionMap.MapPointerPressed += OnMapPointerPressed;
        SelectionMap.MapPointerMoved += OnMapPointerMoved;
        SelectionMap.MapPointerReleased += OnMapPointerReleased;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        _currentViewModel?.PropertyChanged -= OnViewModelPropertyChanged;

        _currentViewModel = DataContext as DownloadOfflineMapViewModel;
        if (_currentViewModel is null)
        {
            return;
        }

        _currentViewModel.PropertyChanged += OnViewModelPropertyChanged;
        SetPanLock(_currentViewModel.IsDrawMode);

        if (_currentViewModel.InitialLatitude is double latitude &&
            _currentViewModel.InitialLongitude is double longitude)
        {
            (double x, double y) = SphericalMercator.FromLonLat(longitude, latitude);
            _pendingCenter = new MPoint(x, y);
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_currentViewModel is null)
        {
            return;
        }

        if (e.PropertyName == nameof(DownloadOfflineMapViewModel.IsDrawMode))
        {
            SetPanLock(_currentViewModel.IsDrawMode);
        }
        else if (e.PropertyName == nameof(DownloadOfflineMapViewModel.HasSelection) &&
                 !_currentViewModel.HasSelection)
        {
            ClearSelectionLayer();
        }
    }

    private void SetPanLock(bool drawMode)
    {
        if (SelectionMap.Map?.Navigator is { } navigator)
        {
            navigator.PanLock = drawMode;
        }
    }

    private void OnMapPointerPressed(object? sender, MapEventArgs e)
    {
        if (_currentViewModel is not { IsDrawMode: true })
        {
            return;
        }

        _drawing = true;
        _drawStartWorld = e.WorldPosition;
        // Do not set Handled here: the MapControl must capture the pointer so the
        // move/release events that complete the drag are still delivered.
    }

    private void OnMapPointerMoved(object? sender, MapEventArgs e)
    {
        if (!_drawing || _drawStartWorld is not { } start)
        {
            return;
        }

        DrawRectangle(start, e.WorldPosition);
        e.Handled = true; // Stop the map from panning while the user draws.
    }

    private void OnMapPointerReleased(object? sender, MapEventArgs e)
    {
        if (!_drawing || _drawStartWorld is not { } start)
        {
            return;
        }

        _drawing = false;
        _drawStartWorld = null;

        MPoint end = e.WorldPosition;
        double minX = Math.Min(start.X, end.X);
        double minY = Math.Min(start.Y, end.Y);
        double maxX = Math.Max(start.X, end.X);
        double maxY = Math.Max(start.Y, end.Y);

        // A tap (no drag distance) is not a selection; clear any stale rectangle.
        if (maxX - minX <= double.Epsilon || maxY - minY <= double.Epsilon)
        {
            ClearSelectionLayer();
            return;
        }

        (double minLongitude, double minLatitude) = SphericalMercator.ToLonLat(minX, minY);
        (double maxLongitude, double maxLatitude) = SphericalMercator.ToLonLat(maxX, maxY);

        _currentViewModel!.SetSelection(minLongitude, minLatitude, maxLongitude, maxLatitude);

        // A completed drag ends draw mode, returning the map to pan navigation.
        if (_currentViewModel.IsDrawMode)
        {
            _currentViewModel.ToggleDrawModeCommand.Execute(null);
        }
    }

    private void DrawRectangle(MPoint start, MPoint end)
    {
        double minX = Math.Min(start.X, end.X);
        double minY = Math.Min(start.Y, end.Y);
        double maxX = Math.Max(start.X, end.X);
        double maxY = Math.Max(start.Y, end.Y);

        Coordinate[] coordinates =
        [
            new Coordinate(minX, minY),
            new Coordinate(maxX, minY),
            new Coordinate(maxX, maxY),
            new Coordinate(minX, maxY),
            new Coordinate(minX, minY),
        ];
        LinearRing ring = new(coordinates);

        GeometryFeature feature = new()
        {
            Geometry = new Polygon(ring),
        };
        feature.Styles.Add(new VectorStyle
        {
            Fill = new Brush(Color.FromArgb(60, 0, 122, 255)),
            Outline = new Pen(Color.FromArgb(220, 0, 122, 255), 2),
        });

        _selectionLayer.Clear();
        _selectionLayer.Add(feature);
        _selectionLayer.DataHasChanged();
    }

    private void ClearSelectionLayer()
    {
        _selectionLayer.Clear();
        _selectionLayer.DataHasChanged();
    }

    private void TryApplyPendingCenter()
    {
        if (_pendingCenter is not { } point)
        {
            return;
        }

        Navigator? navigator = SelectionMap.Map?.Navigator;
        if (navigator is null || navigator.Viewport.Width <= 0 || navigator.Viewport.Height <= 0)
        {
            return;
        }

        _pendingCenter = null;

        if (navigator.Resolutions.Count > 9)
        {
            navigator.CenterOnAndZoomTo(point, navigator.Resolutions[9], 350, Easing.CubicOut);
        }
        else
        {
            navigator.CenterOn(point, 350, Easing.CubicOut);
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && DataContext is DownloadOfflineMapViewModel viewModel)
        {
            if (viewModel.CloseCommand.CanExecute(null))
            {
                viewModel.CloseCommand.Execute(null);
            }

            e.Handled = true;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        GC.SuppressFinalize(this);
        _currentViewModel?.PropertyChanged -= OnViewModelPropertyChanged;
        _currentViewModel = null;

        SelectionMap.MapPointerPressed -= OnMapPointerPressed;
        SelectionMap.MapPointerMoved -= OnMapPointerMoved;
        SelectionMap.MapPointerReleased -= OnMapPointerReleased;
        KeyDown -= OnKeyDown;

        _selectionLayer.Dispose();
    }
}
