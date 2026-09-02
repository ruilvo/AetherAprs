// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using CommunityToolkit.Mvvm.ComponentModel;

namespace AetherAprs.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    [ObservableProperty]
    public partial string Title { get; set; } = "Settings";


    [ObservableProperty]
    public partial string Description { get; set; } = "Configure your AetherAprs preferences";

}
