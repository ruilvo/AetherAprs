// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Services.UI;
using CommunityToolkit.Mvvm.Input;
using Mapsui;
using System;

namespace AetherAprs.ViewModels.Components;

/// <summary>
/// ViewModel for map UI interactions (zoom, pan, center commands).
/// Map layers are provided by services.
/// </summary>
public partial class MapViewModel : ViewModelBase
{
    private readonly IUserLocationLayerService _userLocationLayerService;

    public event EventHandler<MPoint>? CenterOnPointRequested;

    /// <summary>
    /// Raised when the user asks to zoom the map in one level.
    /// </summary>
    public event EventHandler? ZoomInRequested;

    /// <summary>
    /// Raised when the user asks to zoom the map out one level.
    /// </summary>
    public event EventHandler? ZoomOutRequested;

    public MapViewModel(IUserLocationLayerService userLocationLayerService)
    {
        _userLocationLayerService = userLocationLayerService;
    }

    [RelayCommand]
    public void CenterOnUser()
    {
        var mapPoint = _userLocationLayerService.CurrentMapPoint;
        if (mapPoint != null)
        {
            CenterOnPointRequested?.Invoke(this, mapPoint);
        }
    }

    [RelayCommand]
    private void ZoomIn() => ZoomInRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void ZoomOut() => ZoomOutRequested?.Invoke(this, EventArgs.Empty);
}
