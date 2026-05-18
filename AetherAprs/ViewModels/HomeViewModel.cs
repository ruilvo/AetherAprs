// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Text;

namespace AetherAprs.ViewModels;

public partial class HomeViewModel : ViewModelBase
{
    private readonly INavigationService _navService;

    // This generates the public property 'InputText'
    [ObservableProperty]
    public partial string? InputText { get; set; }

    public HomeViewModel(INavigationService navService)
    {
        _navService = navService;

        Title = "Home Station";
        InputText = "Hello APRS!"; // Default initial text
    }
}
