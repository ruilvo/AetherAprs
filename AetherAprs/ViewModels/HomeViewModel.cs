// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using CommunityToolkit.Mvvm.ComponentModel;

namespace AetherAprs.ViewModels;

public partial class HomeViewModel : ViewModelBase
{
    // This generates the public property 'InputText'
    [ObservableProperty]
    public partial string? InputText { get; set; }

    public HomeViewModel()
    {
        Title = "Home Station";
        InputText = "Hello APRS!"; // Default initial text
    }
}
