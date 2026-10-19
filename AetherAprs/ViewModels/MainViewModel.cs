// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Services.UI;
using AetherAprs.ViewModels.Pages;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;

namespace AetherAprs.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly INavigationService _navigationService;
    private readonly IKeyboardInsetsService _keyboardInsetsService;
    private ViewModelBase? _previousOverlay;
    
    private static readonly Dictionary<int, Type> TabIndexToViewModelType = new()
    {
        [0] = typeof(HomeViewModel),
        [1] = typeof(MessagesViewModel),
        [2] = typeof(PacketsViewModel),
        [3] = typeof(PortsViewModel),
        [4] = typeof(SettingsViewModel)
    };

    private static readonly Dictionary<Type, int> ViewModelTypeToTabIndex = new()
    {
        [typeof(HomeViewModel)] = 0,
        [typeof(MessagesViewModel)] = 1,
        [typeof(PacketsViewModel)] = 2,
        [typeof(PortsViewModel)] = 3,
        [typeof(SettingsViewModel)] = 4
    };

    [ObservableProperty]
    public partial int SelectedTabIndex { get; set; } = 0;

    [ObservableProperty]
    public partial ViewModelBase? OverlayViewModel { get; set; }

    public bool IsOverlayVisible => OverlayViewModel != null;

    public HomeViewModel HomeViewModel { get; }
    public MessagesViewModel MessagesViewModel { get; }
    public PacketsViewModel PacketsViewModel { get; }
    public PortsViewModel PortsViewModel { get; }
    public SettingsViewModel SettingsViewModel { get; }

    public MainViewModel(
        INavigationService navService,
        IKeyboardInsetsService keyboardInsetsService,
        HomeViewModel homeViewModel,
        MessagesViewModel messagesViewModel,
        PacketsViewModel packetsViewModel,
        PortsViewModel portsViewModel,
        SettingsViewModel settingsViewModel)
    {
        _navigationService = navService;
        _keyboardInsetsService = keyboardInsetsService;
        HomeViewModel = homeViewModel;
        MessagesViewModel = messagesViewModel;
        PacketsViewModel = packetsViewModel;
        PortsViewModel = portsViewModel;
        SettingsViewModel = settingsViewModel;

        _navigationService.CurrentViewModelChanged += OnCurrentViewModelChanged;
        _navigationService.NavigateTo<HomeViewModel>();
    }

    /// <summary>
    /// Wires the keyboard insets service to the window's TopLevel. Called by
    /// MainView once its TopLevel is available; avoids service-locator use in the view.
    /// </summary>
    public void InitializeKeyboardInsets(TopLevel topLevel)
    {
        _keyboardInsetsService.Initialize(topLevel);
    }

    partial void OnOverlayViewModelChanged(ViewModelBase? value)
    {
        // Retire the previous overlay: transient overlay view models (e.g.
        // ConversationViewModel) subscribe to long-lived services and must detach
        // when they are replaced, or they leak.
        if (_previousOverlay is IOverlayRetireable retired)
        {
            retired.OnRetiredFromOverlay();
        }

        _previousOverlay = value;
        OnPropertyChanged(nameof(IsOverlayVisible));
    }

    partial void OnSelectedTabIndexChanged(int value)
    {
        if (IsOverlayVisible)
        {
            return;
        }

        if (TabIndexToViewModelType.TryGetValue(value, out var viewModelType))
        {
            if (viewModelType == typeof(HomeViewModel))
                _navigationService.NavigateTo<HomeViewModel>();
            else if (viewModelType == typeof(MessagesViewModel))
                _navigationService.NavigateTo<MessagesViewModel>();
            else if (viewModelType == typeof(PacketsViewModel))
                _navigationService.NavigateTo<PacketsViewModel>();
            else if (viewModelType == typeof(PortsViewModel))
                _navigationService.NavigateTo<PortsViewModel>();
            else if (viewModelType == typeof(SettingsViewModel))
                _navigationService.NavigateTo<SettingsViewModel>();
        }
    }

    private void OnCurrentViewModelChanged(object? sender, ViewModelBase? viewModel)
    {
        if (viewModel != null && ViewModelTypeToTabIndex.TryGetValue(viewModel.GetType(), out var tabIndex))
        {
            OverlayViewModel = null;
            SelectedTabIndex = tabIndex;
            return;
        }

        OverlayViewModel = viewModel;
    }
}

/// <summary>
/// Implemented by transient overlay view models that must release event
/// subscriptions when they are removed from the overlay.
/// </summary>
public interface IOverlayRetireable
{
    void OnRetiredFromOverlay();
}
