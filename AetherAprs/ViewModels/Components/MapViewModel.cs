// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Services.UI;
using CommunityToolkit.Mvvm.ComponentModel;
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
    /// Raised when the user asks to reset map rotation to North (0 degrees).
    /// </summary>
    public event EventHandler? RotateToNorthRequested;

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
            // Enable follow mode and center on user
            IsFollowModeEnabled = true;
            CenterOnPointRequested?.Invoke(this, mapPoint);
            RotateToNorthRequested?.Invoke(this, EventArgs.Empty);
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
