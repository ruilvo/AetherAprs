// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;

using AetherAprs.Services.UI;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Mapsui;

namespace AetherAprs.ViewModels.Components;

/// <summary>
/// ViewModel for map UI interactions (zoom, pan, center commands).
/// Map layers are provided by services.
/// </summary>
public partial class MapViewModel(IUserLocationLayerService userLocationLayerService) : ViewModelBase
{
    private readonly IUserLocationLayerService _userLocationLayerService = userLocationLayerService;

    public event EventHandler<MPoint>? CenterOnPointRequested;

    /// <summary>
    /// Raised when the user asks to zoom the map in one level.
    /// </summary>
    public event EventHandler? ZoomInRequested;

    /// <summary>
    /// Raised when the user asks to zoom the map out one level.
    /// </summary>
    public event EventHandler? ZoomOutRequested;

    /// <summary>
    /// Gets or sets whether the map should follow the user's location and bearing.
    /// When enabled, the map automatically centers on the user and rotates based on their direction.
    /// </summary>
    [ObservableProperty]
    public partial bool IsFollowModeEnabled { get; set; }

    /// <summary>
    /// Gets or sets whether the user has manually interacted with the map.
    /// Used to control FAB visibility/opacity.
    /// </summary>
    [ObservableProperty]
    public partial bool HasUserMovedMap { get; set; }

    [RelayCommand]
    public void CenterOnUser()
    {
        MPoint? mapPoint = _userLocationLayerService.CurrentMapPoint;
        if (mapPoint != null)
        {
            // Enable follow mode and center on user. Centering, zooming and
            // resetting the rotation north are applied as one viewport change by
            // the view, so a separate rotation request is unnecessary and would
            // replace the centering animation.
            IsFollowModeEnabled = true;
            CenterOnPointRequested?.Invoke(this, mapPoint);
        }
    }

    /// <summary>
    /// Called when the user manually pans, zooms, or rotates the map.
    /// Disables follow mode and marks that the user has moved the map.
    /// </summary>
    public void OnUserInteraction()
    {
        IsFollowModeEnabled = false;
        HasUserMovedMap = true;
    }

    [RelayCommand]
    private void ZoomIn() => ZoomInRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void ZoomOut() => ZoomOutRequested?.Invoke(this, EventArgs.Empty);
}
